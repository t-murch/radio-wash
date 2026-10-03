using System.Data.Common;
using System.Net;
using RadioWash.Api.Infrastructure.Hangfire;
using RadioWash.Api.Models.Music;
using RadioWash.Api.Services.Exceptions;

namespace RadioWash.Api.Tests.Unit.Infrastructure;

public class JobRetryPolicyTests
{
  public static TheoryData<Exception, bool> Failures => new()
  {
    { new HttpRequestException("504", null, HttpStatusCode.GatewayTimeout), true },
    { new HttpRequestException("500", null, HttpStatusCode.InternalServerError), true },
    { new HttpRequestException("429", null, HttpStatusCode.TooManyRequests), true },
    { new HttpRequestException("connection reset"), true },
    { new TaskCanceledException("HttpClient.Timeout elapsed", new TimeoutException()), true },
    { new FakeDbException(isTransient: true), true },

    { new HttpRequestException("404", null, HttpStatusCode.NotFound), false },
    { new HttpRequestException("400", null, HttpStatusCode.BadRequest), false },
    { new UnauthorizedAccessException("Apple Music authorization expired"), false },
    { new InvalidOperationException("User 7 not found"), false },
    { new OperationCanceledException(), false },
    { new FakeDbException(isTransient: false), false },
  };

  [Theory]
  [MemberData(nameof(Failures))]
  public void IsTransient_ClassifiesFailuresByWhetherARetryCanHelp(Exception failure, bool expected)
  {
    Assert.Equal(expected, JobRetryPolicy.IsTransient(failure));
  }

  [Theory]
  [InlineData(HttpStatusCode.GatewayTimeout, true)]
  [InlineData(HttpStatusCode.NotFound, false)]
  public void IsTransient_LooksThroughTrackProcessingWrapper(HttpStatusCode status, bool expected)
  {
    var wrapped = new TrackProcessingException(
      33,
      290,
      new MusicTrack("1440829776", "Complexion (A Zulu Love)", true, new[] { new MusicArtist("Kendrick Lamar") }),
      new HttpRequestException("upstream", null, status));

    Assert.Equal(expected, JobRetryPolicy.IsTransient(wrapped));
  }

  [Theory]
  [InlineData(0, 30)]
  [InlineData(1, 120)]
  public void DelayBeforeRetry_MatchesTheHangfireSchedule(int retryCount, int expectedSeconds)
  {
    Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), JobRetryPolicy.DelayBeforeRetry(retryCount));
  }

  private sealed class FakeDbException : DbException
  {
    private readonly bool _isTransient;

    public FakeDbException(bool isTransient) : base("db") => _isTransient = isTransient;

    public override bool IsTransient => _isTransient;
  }
}
