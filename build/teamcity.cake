// Copyright © 2022 Chocolatey Software, Inc
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
// You may obtain a copy of the License at
//
// 	http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

///////////////////////////////////////////////////////////////////////////////
// BUILD PROVIDER
///////////////////////////////////////////////////////////////////////////////

public class TeamCityTagInfo : ITagInfo
{
    public TeamCityTagInfo(ICakeContext context)
    {
        // Test to see if current commit is a tag...
        context.Information("Testing to see if current commit contains a tag...");
        IEnumerable<string> redirectedStandardOutput;
        IEnumerable<string> redirectedError;

        var exitCode = context.StartProcess(
            "git",
            new ProcessSettings {
                Arguments = "tag -l --points-at HEAD",
                RedirectStandardOutput = true,
                RedirectStandardError = true
            },
            out redirectedStandardOutput,
            out redirectedError
        );

        if (exitCode == 0)
        {
            var outputLines = redirectedStandardOutput.ToList();
            if (outputLines.Count != 0)
            {
                IsTag = true;
                Name = outputLines.FirstOrDefault();

                context.Information("Command output:");
                foreach(var line in outputLines)
                {
                    context.Information(line);
                }
            }
        }
    }

    public bool IsTag { get; }

    public string Name { get; }
}

public class TeamCityRepositoryInfo : IRepositoryInfo
{
    public TeamCityRepositoryInfo(ITeamCityProvider teamCity, ICakeContext context)
    {
        Name = teamCity.Environment.Build.BuildConfName;

        string baseRef = null;
        context.Information("BuildConfName is {0}", context.BuildSystem().TeamCity.Environment.Build.BuildConfName);
        context.Information("BuildNumber is {0}", context.BuildSystem().TeamCity.Environment.Build.Number);

        if (!string.IsNullOrEmpty(baseRef))
        {
            Branch = baseRef;
        }
        else
        {
            // This trimming is not perfect, as it will remove part of a
            // branch name if the branch name itself contains a '/'
            var tempName = context.Environment.GetEnvironmentVariable("Git_Branch");

            context.Information("Git_Branch is {0}", tempName);

            const string headPrefix = "refs/heads/";
            const string tagPrefix = "refs/tags/";

            if (!string.IsNullOrEmpty(tempName))
            {
                context.Information("Trimming branch name if it starts with {0} or {1}...", headPrefix, tagPrefix);
                if (tempName.StartsWith(headPrefix))
                {
                    context.Information("Branch name starts with {0}, trimming it...", headPrefix);
                    tempName = tempName.Substring(headPrefix.Length);
                }
                else if (tempName.StartsWith(tagPrefix))
                {
                    context.Information("Branch name starts with {0}, trimming it...", tagPrefix);
                    var gitTool = context.Tools.Resolve("git");
                    if (gitTool == null)
                    {
                        gitTool = context.Tools.Resolve("git.exe");
                    }

                    if (gitTool != null)
                    {
                        IEnumerable<string> redirectedStandardOutput;
                        IEnumerable<string> redirectedError;

                        var exitCode = context.StartProcess(
                            gitTool,
                            new ProcessSettings {
                                Arguments = "branch -r --contains " + tempName,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                            },
                            out redirectedStandardOutput,
                            out redirectedError
                        );

                        if (exitCode == 0)
                        {
                            var lines = redirectedStandardOutput.ToList();
                            if (lines.Count != 0)
                            {
                                tempName = lines[0].TrimStart(new []{ ' ', '*' }).Replace("origin/", string.Empty);
                                context.Information("Branch name is {0}", tempName);

                                context.Information("Command output:");
                                foreach(var line in lines)
                                {
                                    context.Information(line);
                                }
                            }
                            else
                            {
                                context.Information("No branches contain the tag {0}, using the tag name as the branch name...", tempName);
                            }
                        }
                        else
                        {
                            context.Error("Unable to find branch name for tag {0}!", tempName);
                            context.Information("Writing out standard out...");
                            var standardOutLines = redirectedStandardOutput.ToList();
                            foreach (var standardOutLine in standardOutLines)
                            {
                                context.Information(standardOutLine);
                            }

                            context.Information("Writing out standard error...");
                            var standardErrorLines = redirectedError.ToList();
                            foreach (var standardErrorLine in standardErrorLines)
                            {
                                context.Information(standardErrorLine);
                            }
                        }
                    }
                }
                else if (tempName.IndexOf('/') >= 0)
                {
                    context.Information("Branch name contains '/', trimming it to the last segment...");
                    tempName = tempName.Substring(tempName.LastIndexOf('/') + 1);
                }
            }

            context.Information("Branch name is {0}", tempName);
            Branch = tempName;
        }

        Tag = new TeamCityTagInfo(context);
    }

    public string Branch { get; }

    public string Name { get; }

    public ITagInfo Tag { get; }
}

public class TeamCityPullRequestInfo : IPullRequestInfo
{
    public TeamCityPullRequestInfo(ITeamCityProvider teamCity)
    {
        IsPullRequest = teamCity.Environment.PullRequest.IsPullRequest;
    }

    public bool IsPullRequest { get; }
}

public class TeamCityBuildInfo : IBuildInfo
{
    public TeamCityBuildInfo(ITeamCityProvider teamCity)
    {
        Number = teamCity.Environment.Build.Number;
    }

    public string Number { get; }
}

public class TeamCityBuildProvider : IBuildProvider
{
    public TeamCityBuildProvider(ITeamCityProvider teamCity, ICakeContext context)
    {
        Build = new TeamCityBuildInfo(teamCity);
        PullRequest = new TeamCityPullRequestInfo(teamCity);
        Repository = new TeamCityRepositoryInfo(teamCity, context);

        _teamCity = teamCity;
        _context = context;
    }

    public IRepositoryInfo Repository { get; }

    public IPullRequestInfo PullRequest { get; }

    public IBuildInfo Build { get; }

    public bool SupportsTokenlessCodecov { get; } = false;

    public BuildProviderType Type { get; } = BuildProviderType.TeamCity;

    public IEnumerable<string> PrintVariables { get; } = new[] {
        "TEAMCITY_BUILD_BRANCH",
        "TEAMCITY_BUILD_COMMIT",
        "TEAMCITY_BUILD_ID",
        "TEAMCITY_BUILD_REPOSITORY",
        "TEAMCITY_BUILD_URL",
        "TEAMCITY_VERSION",
        "vcsroot.branch",
    };

    private readonly ITeamCityProvider _teamCity;

    private readonly ICakeContext _context;

    public void UploadArtifact(FilePath file)
    {
        _context.Information("Uploading artifact from path: {0}", file.FullPath);
        _teamCity.PublishArtifacts(file.FullPath);
    }
}