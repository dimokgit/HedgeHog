using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HedgeHog.CloudTests {
  [TestClass]
  public class GistTests {
    [TestMethod]
    public async Task  GistList() {
      var gistName = "test.txt";
      await Cloud.GitHub.GistStrategyDeleteByName(gistName);
      Assert.AreEqual(0, (await Cloud.GitHub.GistStrategyFindByName(gistName)).Count());
      var gistDescription = "Decription ";
      var gistContent = DateTime.Now + "";
      var newGist = await Cloud.GitHub.GistStrategyAddOrUpdate(gistName, gistDescription, gistContent);
      var gists = await Cloud.GitHub.GistStrategies((name, rawUrl, content) => new {rawUrl, name, content });
      Console.WriteLine(string.Join("\n", gists.Select(gist => gist.ToString())));
      Assert.AreEqual(1, gists.Count(gist => gist.name == gistName));
      Assert.AreEqual(1, gists.Count(gist => gist.content == gistContent));

      gistContent = DateTime.Now.AddDays(1) + "";
      newGist = await Cloud.GitHub.GistStrategyAddOrUpdate(gistName, gistDescription, gistContent);
      gists = await Cloud.GitHub.GistStrategies(( name, rawUrl, content) => new { rawUrl, name, content });
      Console.WriteLine(string.Join("\n", gists.Select(gist => gist.ToString())));
      Assert.AreEqual(1, gists.Count(gist => gist.name == gistName));
      Assert.AreEqual(1, (await Cloud.GitHub.GistStrategyFindByName(gistName)).SelectMany(gist=>gist.Files) .Count(gist => gist.Value.Content == gistContent));
    }
  }
}
