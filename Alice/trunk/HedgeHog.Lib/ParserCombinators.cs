using System;
using System.Linq;

// The result of a parse consists of a value and the unconsumed input.
public class Result<TInput, TValue> {
  public readonly TValue Value;
  public readonly TInput Rest;
  public Result(TValue value, TInput rest) { Value = value; Rest = rest; }
}

// A Parser is a delegate which takes an input and returns a result.
public delegate Result<TInput, TValue> Parser<TInput, TValue>(TInput input);

public static class ParserCombinatorExtensions {
  public static Parser<TInput, TValue> OR<TInput, TValue>(this Parser<TInput, TValue> parser1, Parser<TInput, TValue> parser2) {
    return input => parser1(input) ?? parser2(input);
  }
  public static Parser<TInput, TValue2> AND<TInput, TValue1, TValue2>(this Parser<TInput, TValue1> parser1, Parser<TInput, TValue2> parser2) {
    return input => parser2(parser1(input).Rest);
  }
}

// Expose some operations on the Parser type using Extension methods.
public static class ParserCombinatorsMonad {
  // By providing Select, Where and SelecMany methods on Parser<TInput,TValue> we make the 
  // C# Query Expression syntax available for manipulating Parsers.  
  public static Parser<TInput, TValue> Where<TInput, TValue>(this Parser<TInput, TValue> parser, Func<TValue, bool> pred) {
    return input => {
      var res = parser(input);
      if (res == null || !pred(res.Value)) return null;
      return res;
    };
  }
  public static Parser<TInput, TValue2> Select<TInput, TValue, TValue2>(this Parser<TInput, TValue> parser, Func<TValue, TValue2> selector) {
    return input => {
      var res = parser(input);
      if (res == null) return null;
      return new Result<TInput, TValue2>(selector(res.Value), res.Rest);
    };
  }
  public static Parser<TInput, TValue2> SelectMany<TInput, TValue, TIntermediate, TValue2>(
      this Parser<TInput, TValue> parser
    , Func<TValue, Parser<TInput, TIntermediate>> selector
    , Func<TValue, TIntermediate, TValue2> projector) {
    return input => {
      var res = parser(input);
      if (res == null) return null;
      var val = res.Value;
      var res2 = selector(val)(res.Rest);
      if (res2 == null) return null;
      return new Result<TInput, TValue2>(projector(val, res2.Value), res2.Rest);
    };
  }
}

// Contains all the basic parsers that are independent of return type.
public abstract class Parsers<TInput> {
  public Parser<TInput, TValue> Succeed<TValue>(TValue value) {
    return input => new Result<TInput, TValue>(value, input);
  }
  public Parser<TInput, TValue[]> Rep<TValue>(Parser<TInput, TValue> parser) {
    return Rep1(parser).OR(Succeed(new TValue[0]));
  }
  public Parser<TInput, TValue[]> Rep1<TValue>(Parser<TInput, TValue> parser) {
    var xs1 = new Func<TValue, Parser<TInput, TValue[]>>(x => Rep(parser));
    return parser.SelectMany(x => Rep(parser), (x, xs) => (new[] { x }.Concat(xs)).ToArray());
    //return from x in parser
    //       from xs in Rep(parser)
    //       select (new[] { x }).Concat(xs).ToArray();
  }
}

// Contains a few parsers that parse characters from an input stream
public abstract class CharParsers<TInput> : Parsers<TInput> {
  public abstract Parser<TInput, char> AnyChar { get; }
  public Parser<TInput, char> Char(char ch) { return from c in AnyChar where c == ch select c; }
  public Parser<TInput, char> Char(Predicate<char> pred) { return from c in AnyChar where pred(c) select c; }
}

// The Term class and its derived classes define the AST for terms in the MiniML langauge.
public abstract class Term { }
public class LambdaTerm : Term { public readonly string Ident; public readonly Term Term; public LambdaTerm(string i, Term t) { Ident = i; Term = t; } }
public class LetTerm : Term { public readonly string Ident; public readonly Term Rhs; public Term Body; public LetTerm(string i, Term r, Term b) { Ident = i; Rhs = r; Body = b; } }
public class AppTerm : Term { public readonly Term Func; public readonly Term[] Args; public AppTerm(Term func, Term[] args) { Func = func; Args = args; } }
public class VarTerm : Term { public readonly string Ident; public VarTerm(string ident) { Ident = ident; } }

// Provides a set of parsers for the MiniML Language defined above.  
public abstract class MiniMLParsers<TInput> : CharParsers<TInput> {
  public MiniMLParsers() {
    Whitespace = Rep(Char(' ').OR(Char('\t').OR(Char('\n')).OR(Char('\r'))));
    WsChr = chr => Whitespace.AND(Char(chr));
    Id = from w in Whitespace
         from c in Char(char.IsLetter)
         from cs in Rep(Char(char.IsLetterOrDigit))
         select cs.Aggregate(c.ToString(), (acc, ch) => acc + ch);
    Ident = from s in Id where s != "let" && s != "in" select s;
    LetId = from s in Id where s == "let" select s;
    InId = from s in Id where s == "in" select s;
    Term1 = (from x in Ident
             select (Term)new VarTerm(x))
            .OR(
            (from u1 in WsChr('(')
             from t in Term
             from u2 in WsChr(')')
             select t));
    Term = (from u1 in WsChr('\\')
            from x in Ident
            from u2 in WsChr('.')
            from t in Term
            select (Term)new LambdaTerm(x, t))
            .OR(
            (from letid in LetId
             from x in Ident
             from u1 in WsChr('=')
             from t in Term
             from inid in InId
             from c in Term
             select (Term)new LetTerm(x, t, c)))
            .OR(
            (from t in Term1
             from ts in Rep(Term1)
             select (Term)new AppTerm(t, ts)));
    All = from t in Term from u in WsChr(';') select t;
  }

  public Parser<TInput, char[]> Whitespace;
  public Func<char, Parser<TInput, char>> WsChr;
  public Parser<TInput, string> Id;
  public Parser<TInput, string> Ident;
  public Parser<TInput, string> LetId;
  public Parser<TInput, string> InId;
  public Parser<TInput, Term> Term;
  public Parser<TInput, Term> Term1;
  public Parser<TInput, Term> All;
}

public class MiniMLParserFromString : MiniMLParsers<string> {
  public override Parser<string, char> AnyChar {
    get { { return input => input.Length > 0 ? new Result<string, char>(input[0], input.Substring(1)) : null; } }
  }
}

class Program {
  static void Main(string[] args) {
    MiniMLParserFromString parser = new MiniMLParserFromString();
    Result<string, Term> result =
        parser.All(@"let true = \x.\y.x in 
                         let false = \x.\y.y in 
                         let if = \b.\l.\r.(b l) r in
                         if true then false else true;");
  }
}
