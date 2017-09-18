using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using HedgeHog.Cloud;
using Octokit;

namespace HedgeHog.CloudTests {
  [TestClass]
  public class GistTests {
    [TestMethod]
    public async Task LoadGitFile() {
      var ghClient = Cloud.GitHub.ClientFactory();
      ghClient.Credentials = new Octokit.Credentials("dimokgit", "BudG0tov");

      // github variables
      var owner = "dimokgit";
      var repo = "HedgeHog";
      var branch = "master";

      var targetFile = "HedgeHog.Alice.Client/Settings/Instruments.json";

      try {
        // try to get the file (and with the file the last commit sha)
        var existingFile = await ghClient.Repository.Content.GetAllContentsByRef(owner, repo, targetFile, branch);

        // update the file
        var updateChangeSet = await ghClient.Repository.Content.UpdateFile(owner, repo, targetFile,
           new UpdateFileRequest("Instruments Update "+DateTime.Now, "Instruments Update " + DateTime.Now, existingFile.First().Sha, branch));
      } catch(Octokit.NotFoundException) {
        // if file is not found, create it
        var createChangeSet = await ghClient.Repository.Content.CreateFile(owner, repo, targetFile, new CreateFileRequest("API File creation", "Hello Universe! " + DateTime.UtcNow, branch));
      }
    }

    [TestMethod]
    public async Task GistList() {
      var gistName = "test.txt";
      #region Clean test gists
      await Cloud.GitHub.GistStrategyDeleteByName(gistName, true);
      Assert.AreEqual(0, (await Cloud.GitHub.GistStrategyFindByName(gistName)).Count());
      await Cloud.GitHub.GistStrategyDeleteByName(gistName, true,true);
      Assert.AreEqual(0, (await Cloud.GitHub.GistStrategyFindByName(gistName, true)).Count());
      #endregion
      #region Run main tests
      var gistDescription = "Decription ";
      var gistContent = DateTime.Now + "";
      Func<string> gistContent2 = () => gistContent + "(2)";
      var newGist = await Cloud.GitHub.GistStrategyAddOrUpdate(gistName, gistDescription, gistContent, gistContent2());
      var gists = await Cloud.GitHub.GistStrategyFindByName(gistName);
      Console.WriteLine(string.Join("\n", gists.Select(gist => gist.ToString())));
      Assert.AreEqual(1, gists.Count(gist => gist.Strategy() == gistName));
      Assert.AreEqual(1, gists.SelectMany(gist => gist.Files).Count(file => file.Value.Content == gistContent));
      Assert.AreEqual(1, gists.SelectMany(gist => gist.Files).Count(file => file.Value.Content == gistContent2()));

      gistContent = DateTime.Now.AddDays(1) + "";
      newGist = await Cloud.GitHub.GistStrategyAddOrUpdate(gistName, gistDescription, gistContent, gistContent2());
      gists = await Cloud.GitHub.GistStrategyFindByName(gistName);
      Console.WriteLine(string.Join("\n", gists.Select(gist => gist.ToString())));
      Assert.AreEqual(1, gists.Count(gist => gist.Strategy() == gistName));
      Assert.AreEqual(1, gists.SelectMany(gist => gist.Files).Count(file => file.Value.Content == gistContent));
      Assert.AreEqual(1, gists.SelectMany(gist => gist.Files).Count(file => file.Value.Content == gistContent2()));
      #endregion
      #region archive/restore/delete
      await Cloud.GitHub.GistStrategyDeleteByName(gistName, false);
      Assert.AreEqual(1, (await Cloud.GitHub.GistStrategyFindByName(gistName,true)).Count());

      await Cloud.GitHub.GistStrategyRestoreByName(gistName);
      Assert.AreEqual(1, (await Cloud.GitHub.GistStrategyFindByName(gistName)).Count());
      await Cloud.GitHub.GistStrategyDeleteByName(gistName, true);
      Assert.AreEqual(0, (await Cloud.GitHub.GistStrategyFindByName(gistName)).Count());
      #endregion
    }
  }
}
