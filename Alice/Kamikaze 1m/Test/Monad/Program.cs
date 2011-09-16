using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Monad {
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
}
