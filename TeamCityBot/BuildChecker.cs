﻿using System;
using System.Collections.Generic;
using System.Linq;
using HelloBotCommunication;
using TeamCitySharp;
using TeamCitySharp.Locators;

namespace TeamCityBot
{
    public class TeamCityBuildChecker : ITeamCityBuildChecker
    {
        private static readonly Random _r = new Random();
        private string _lastBastard;
        private string _lastCheckedBuildId;
        private DateTime? _lastFailedTime;
        private string _lastReason;
        private TestOccurrencesCollection _lastTimeTests;
        private bool _wasBroken;
        private readonly BuildLocator _buildLocator;
        private readonly ITeamCityClient _client;
        private readonly string _name;
        private readonly TimeConfig _timeConfig;
	    private bool _muted;

		public string Branch { get; private set; }

        public TeamCityBuildChecker(BuildLocator buildLocator, ITeamCityClient client, string name,
            TimeConfig timeConfig)
        {
            _buildLocator = buildLocator;
            _client = client;
            _name = name;
            _timeConfig = timeConfig;

	        Branch = name;
        }

        public void CheckBuild(Action<BuildResult> sendMessage)
        {
            var build = _client.Builds.ByBuildLocator(_buildLocator).FirstOrDefault();

            if (build == null)
            {
                Console.WriteLine("No builds found by given locator");
            }
            else if (build.Id == _lastCheckedBuildId)
            {
                Console.WriteLine("Build {0} - already checked last time", build.Id);
            }
            else
            {
                Console.WriteLine("Found new build {0}: {1}", build.Number, build.Status);

                var changes = _client.Changes.ByLocator(
                    ChangeLocator.WithBuildId(long.Parse(build.Id)))
                    .FirstOrDefault();
                var author = changes != null ? changes.Username : "<anonymous>";

                if (build.Status != "SUCCESS")
                {
                    var exactBuild = _client.Builds.ByBuildId(build.Id);
                    var reason = exactBuild.StatusText;

                    var testOccurrences =
                        new TestOccurrencesCollection(_client.TestOccurrences.ByBuildId(build.Id, 1500));

                    var now = DateTimeProvider.UtcNow;
                    if ((!_lastFailedTime.HasValue ||
                         (now - _lastFailedTime.Value) >= _timeConfig.StillBrokenDelay)
                        //||
                        //(String.IsNullOrEmpty(_lastReason) || _lastReason != reason)
                        //||
                        /*(_lastTimeTests == null || !testOccurrences.EqualsByFailed(_lastTimeTests))*/)
                    {
                        _lastCheckedBuildId = build.Id;
                        var failReason = GetReason(reason);

                        BuildResult buildResult;
                        if (!_wasBroken)
                        {
                            string detailedReason;

                            if (failReason == FailReason.Tests)
                            {
                                detailedReason = testOccurrences.Show();
                                _lastTimeTests = testOccurrences;
                            }
                            else
                            {
                                detailedReason = reason; //TODO: get error message from build log
                            }

                            _lastBastard = author;
                            _wasBroken = true;

                            buildResult = new BuildResult
                            {
                                Number = build.Number,
                                Branch = _name,
                                Author = _lastBastard,
                                ReasonText = reason,
                                WebUrl = build.WebUrl,
                                DetailedReason = detailedReason,
                                Status = BuildStatus.Broken
                            };
                        }
                        else
                        {
                            var detailedReason = "";

                            if (failReason == FailReason.Tests)
                            {
                                var diff = _lastTimeTests.Diff(testOccurrences);
                                detailedReason += diff.Show(true, "New broken tests", true, "Fixed tests");

                                _lastTimeTests = testOccurrences;
                            }
                            else
                            {
                                detailedReason = reason;
                            }

                            buildResult = new BuildResult
                            {
                                Number = build.Number,
                                Branch = _name,
                                Author = _lastBastard,
                                ReasonText = reason,
                                WebUrl = build.WebUrl,
                                DetailedReason = detailedReason,
                                Status = BuildStatus.StillBroken
                            };
                        }

                        _lastFailedTime = now;
                        _lastReason = reason;
	                    if (!_muted)
	                    {
		                    sendMessage(buildResult);
	                    }
                    }
                    else
                    {
                        Console.WriteLine(
                            "Build is broken but it's too early to notify: lastFailedTime={0}, now={1}, stillBrokenDelay={2}",
                            _lastFailedTime.Value.ToString("hh:mm:ss.ffff"),
                            now.ToString("hh:mm:ss.ffff"),
                            _timeConfig.StillBrokenDelay.TotalMilliseconds);
                    }
                }
                else
                {
                    if (_wasBroken)
                    {
                        var result = new BuildResult
                        {
                            Number = build.Number,
                            Branch = _name,
                            Author = author,
                            Status = BuildStatus.Fixed
                        };
                        sendMessage(result);
                        _wasBroken = false;
                    }
                    _lastFailedTime = null;
                    _lastReason = null;
                    _wasBroken = false;
                    _lastTimeTests = null;
                }
            }
        }

        private FailReason GetReason(string status)
        {
            if (status.StartsWith("Tests"))
            {
                return FailReason.Tests;
            }
            return FailReason.Build;
        }

	    public void Mute()
	    {
		    _muted = true;
	    }

	    public void Unmute()
	    {
		    _muted = false;
	    }

	    public void Blame(string person)
	    {
		    if (_wasBroken) _lastBastard = person;
	    }
    }
}