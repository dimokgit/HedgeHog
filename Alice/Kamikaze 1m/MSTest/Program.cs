using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace MSTest {
  class Program {
    static void Main(string[] args) {
      var words = "This is a sentence. Let's reverse it.";
      Console.Write(words + Environment.NewLine);
      Console.Write(ReverseWords(words));
      Console.ReadKey();
    }

    private static string ReverseWords(string sentence, char separator = ' ') {
      var words = sentence.Split(separator);
      return string.Join(separator.ToString(), words.Reverse());
    }
  }
}
