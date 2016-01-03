using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Octokit;

namespace HedgeHog.Cloud {
  public static class GitHub {
    static readonly string prefix = "strategy\\";
    public static string MakeStrategyName(string name) { return prefix + name; }
    public static string CleanStrategyName(string name) { return Regex.Replace(name, "^" + prefix+"\\", "", RegexOptions.IgnoreCase); }
    public static string MyApp { get; set; } = "HedgeHog";
    static GitHubClient ClientFactory() {
      var client = new GitHubClient(new ProductHeaderValue(MyApp));
      client.Credentials = new Octokit.Credentials("hedgehogalice", "2Bbbbbbb");
      return client;
    }
    public static async Task<IList<Gist>> GistStrategies() {
      return (from gist in await ClientFactory().Gist.GetAll()
              where gist.Files.Single().Key.ToLower().StartsWith(prefix)
              select gist
              ).ToArray();
    }
    public static async Task<IEnumerable<T>> GistStrategies<T>(Func<string, string, string, T> map) {
      var gists = await Task.WhenAll(from gist in await GistStrategies()
                                     select ClientFactory().Gist.Get(gist.Id));
      return MapStrategies(gists, map);

    }
    public static async Task<IEnumerable<T>> GistStrategies<T>(Func<string, string, T> map) {
      return MapStrategies((await GistStrategies()).ToArray(),(name,url,content)=> map(name, url));

    }

    private static IEnumerable<T> MapStrategies<T>(Gist[] gists, Func<string, string, string, T> map) {
      return from gist in gists
             from file in gist.Files
             select map(CleanStrategyName(file.Key), gist.Description, file.Value.Content);
    }

    private static async Task<string> FetchGistContent(GistFile file) {
      return await new HttpClient().GetStringAsync(file.RawUrl);
    }

    public static async Task<Gist> GistStrategyAddOrUpdate(string name, string description, string content) {
      IEnumerable<Gist> gistId = await GistStrategyFindByName(name);
      return await (gistId.Any()
        ? GistStrategyUpdate(gistId.First().Id, name, description, content)
        : GistStrategyAdd(name, description, content));
    }

    public static async Task<IEnumerable<Gist>> GistStrategyFindByName(string name) {
      var tasks = (from gist in await GistStrategies()
                   from file in gist.Files
                   where file.Value.Filename.ToLower() == MakeStrategyName(name.ToLower())
                   select ClientFactory().Gist.Get(gist.Id));
      return (await Task.WhenAll(tasks));
    }

    public static async Task<Gist> GistStrategyAdd(string name, string description, string content) {
      var newGist = new NewGist() { Description = description };
      newGist.Files.Add( MakeStrategyName(name), content);
      return await ClientFactory().Gist.Create(newGist);
    }
    public static async Task<Gist> GistStrategyUpdate(string id, string name, string description, string content) {
      var newGist = new GistUpdate() { Description = description };
      newGist.Files.Add(MakeStrategyName(name), new GistFileUpdate { Content = content });
      return await ClientFactory().Gist.Edit(id, newGist);
    }
    public static async Task GistStrategyDeleteByName(string name) {
      var tasks = (await GistStrategyFindByName(name))
        .Select(async gist => await ClientFactory().Gist.Delete(gist.Id));
      await Task.WhenAll(tasks);
    }

  }
}
