using System.Text.Json.Nodes;
using QuickDescribe.Services;

namespace QuickDescribe.Tests;

/// <summary>The helper client against a fake helper: answers matched to requests, restarts, timeouts, giving up.</summary>
public class HelperConnectionTests
{
    private static readonly Func<uint, Task<byte[]>> Jpeg = _ => Task.FromResult(new byte[] { 0xFF, 0xD8 });
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EachRequest_GetsItsOwnAnswer_FromOneHelper()
    {
        var helpers = new FakeHelpers();
        using var ai = helpers.Client();
        Assert.Equal("A description.", await ai.DescribeAsync(Jpeg, "brief", Token));
        Assert.Equal(["Line one", "Line two"], await ai.ReadTextAsync([1, 2], Token));
        var helper = Assert.Single(helpers.Started);
        Assert.Equal(["brief", null], helper.Requests.Select(r => (string?)r["kind"]));
        Assert.Equal("read", (string?)helper.Requests[1]["op"]);
        Assert.NotEqual((int)helper.Requests[0]["id"]!, (int)helper.Requests[1]["id"]!);
    }

    [Fact]
    public async Task AnAnswerToAnotherRequest_RestartsTheHelper()
    {
        var helpers = new FakeHelpers { Setup = h => h.Answer = r => FakeHelper.Ok(new JsonObject { ["id"] = (int)r["id"]! + 100 }) };
        using var ai = helpers.Client();
        var ex = await Assert.ThrowsAsync<DescriptionException>(() => ai.ReadTextAsync([1], Token));
        Assert.Equal("bad_response", ex.Code);
        Assert.True(helpers.Started[0].Killed);

        helpers.Setup = null;
        Assert.Equal(["Line one", "Line two"], await ai.ReadTextAsync([1], Token));
        Assert.Equal(2, helpers.Started.Count); // a fresh one
    }

    [Fact]
    public async Task AHelperThatStopsWithoutAnswering_IsAnInternalError_AndIsReplaced()
    {
        var helpers = new FakeHelpers { Setup = h => h.Answer = _ => null };
        using var ai = helpers.Client();
        var ex = await Assert.ThrowsAsync<DescriptionException>(() => ai.ReadTextAsync([1], Token));
        Assert.Equal("internal_error", ex.Code);
        helpers.Setup = null;
        await ai.ReadTextAsync([1], Token);
        Assert.Equal(2, helpers.Started.Count);
    }

    [Fact]
    public async Task ASlowAnswer_TimesOut_AndTheHelperIsKilled()
    {
        var helpers = new FakeHelpers { Setup = h => h.Hold = new TaskCompletionSource() };
        using var ai = helpers.Client(TimeSpan.FromMilliseconds(200));
        var ex = await Assert.ThrowsAsync<DescriptionException>(() => ai.ReadTextAsync([1], Token));
        Assert.Equal("timeout", ex.Code);
        Assert.True(helpers.Started[0].Killed);
    }

    [Fact]
    public async Task ARequestWaitingBehindOneThatTimedOut_GivesUpAtOnce_AndALaterOneTriesAFreshHelper()
    {
        var helpers = new FakeHelpers { Setup = h => h.Hold = new TaskCompletionSource() };
        using var ai = helpers.Client(TimeSpan.FromMilliseconds(200));
        var describing = ai.DescribeAsync(Jpeg, "brief", Token);
        var reading = ai.ReadTextAsync([1], Token);
        Assert.Equal("timeout", (await Assert.ThrowsAsync<DescriptionException>(() => describing)).Code);
        var waited = await Assert.ThrowsAsync<DescriptionException>(() => reading);
        Assert.Equal("timeout", waited.Code);
        Assert.Equal(WindowsAI.NotRespondingMessage, waited.Message);
        // The read never reached a helper: the hung one was stopped, and no other was started for it.
        Assert.Single(helpers.Started);
        Assert.Single(helpers.Started[0].Requests);

        helpers.Setup = null;
        Assert.Equal(["Line one", "Line two"], await ai.ReadTextAsync([1], Token));
        Assert.Equal(2, helpers.Started.Count);
    }

    [Fact]
    public async Task ARequestWaitingBehindOneGivenUp_StillGoesAhead()
    {
        var hold = new TaskCompletionSource();
        var helpers = new FakeHelpers { Setup = h => h.Hold = hold };
        using var ai = helpers.Client();
        using var cancel = new CancellationTokenSource();
        var first = ai.ReadTextAsync([1], cancel.Token);
        var second = ai.ReadTextAsync([2], Token);
        await Task.Delay(50, Token);
        // Before cancelling: the second request may start its helper the moment the first lets go.
        helpers.Setup = null;
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(["Line one", "Line two"], await second);
        Assert.Equal(2, helpers.Started.Count);
    }

    [Fact]
    public async Task ARequestWaitingBehindAHungWarmUp_GivesUpAtOnce()
    {
        var was = WindowsAI.WarmUpTimeout;
        WindowsAI.WarmUpTimeout = TimeSpan.FromMilliseconds(300);
        try
        {
            var helpers = new FakeHelpers { Setup = h => h.Hold = new TaskCompletionSource() };
            using var ai = helpers.Client(TimeSpan.FromMinutes(3));
            var warming = ai.WarmUpAsync(Token);
            while (helpers.Started.Count == 0 || helpers.Started[0].Requests.Count == 0) await Task.Delay(10, Token);
            var reading = ai.ReadTextAsync([1], Token);
            Assert.Equal("timeout", (await Assert.ThrowsAsync<DescriptionException>(() => warming)).Code);
            Assert.Equal("timeout", (await Assert.ThrowsAsync<DescriptionException>(() => reading)).Code);
            Assert.Single(helpers.Started);
        }
        finally
        {
            WindowsAI.WarmUpTimeout = was;
        }
    }

    [Fact]
    public async Task AWarmUpWaitingItsTurnLongerThanItsLimit_IsntATimeout()
    {
        var was = WindowsAI.WarmUpTimeout;
        WindowsAI.WarmUpTimeout = TimeSpan.FromMilliseconds(200);
        try
        {
            var hold = new TaskCompletionSource();
            var helpers = new FakeHelpers { Setup = h => h.Hold = hold };
            using var ai = helpers.Client();
            // The person's own picture, slow but answered, ahead of the warm-up.
            var reading = ai.ReadTextAsync([1], Token);
            var warming = ai.WarmUpAsync(Token);
            await Task.Delay(500, Token);
            hold.SetResult();
            Assert.Equal(["Line one", "Line two"], await reading);
            await warming;
            Assert.Single(helpers.Started);
            Assert.Equal(3, helpers.Started[0].Requests.Count);
        }
        finally
        {
            WindowsAI.WarmUpTimeout = was;
        }
    }

    [Fact]
    public void TheTextRecognizer_IsLeftAlone_OnlyWhileACheckSaysItNeedsGettingReady()
    {
        Assert.False(WindowsAI.RecognizerNotReady(null));
        Assert.False(WindowsAI.RecognizerNotReady(WindowsAI.ParseReadiness("""{"identity":true,"state":"Ready"}""")));
        Assert.False(WindowsAI.RecognizerNotReady(WindowsAI.ParseReadiness("""{"identity":true,"state":"Ready","components":{"textRecognition":"Ready"}}""")));
        Assert.True(WindowsAI.RecognizerNotReady(WindowsAI.ParseReadiness("""{"identity":true,"state":"Ready","components":{"textRecognition":"NotReady"}}""")));
    }

    [Fact]
    public async Task GivingUp_KillsTheHelper_SoItsLateAnswerCantBeReadAsTheNextOnes()
    {
        var helpers = new FakeHelpers { Setup = h => h.Hold = new TaskCompletionSource() };
        using var ai = helpers.Client();
        using var cancel = new CancellationTokenSource();
        var reading = ai.ReadTextAsync([1], cancel.Token);
        await Task.Delay(50, Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        Assert.True(helpers.Started[0].Killed);
    }

    [Fact]
    public async Task ARequestWaitingBehindAnother_CanBeGivenUp_WithoutTouchingTheHelper()
    {
        var hold = new TaskCompletionSource();
        var helpers = new FakeHelpers { Setup = h => h.Hold = hold };
        using var ai = helpers.Client();
        var first = ai.ReadTextAsync([1], Token);
        using var cancel = new CancellationTokenSource();
        var second = ai.ReadTextAsync([2], cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        hold.SetResult();
        Assert.Equal(["Line one", "Line two"], await first);
        Assert.False(helpers.Started[0].Killed);
        Assert.Single(helpers.Started[0].Requests);
    }

    [Fact]
    public async Task AHelperThatWontStart_IsNotInstalled()
    {
        var helpers = new FakeHelpers { StartFailure = new DescriptionException("not_installed", "The helper didn't start.") };
        using var ai = helpers.Client();
        Assert.Equal("not_installed", (await Assert.ThrowsAsync<DescriptionException>(() => ai.ReadTextAsync([1], Token))).Code);
    }

    [Fact]
    public async Task Disposing_WhileARequestIsInFlight_FailsIt_AndKillsTheHelper()
    {
        var helpers = new FakeHelpers { Setup = h => h.Hold = new TaskCompletionSource() };
        var ai = helpers.Client();
        var reading = ai.ReadTextAsync([1], Token);
        await Task.Delay(50, Token);
        ai.Dispose();
        Assert.Equal("internal_error", (await Assert.ThrowsAsync<DescriptionException>(() => reading)).Code);
        Assert.True(helpers.Started[0].Killed);
        Assert.True(helpers.Started[0].Disposed);
    }

    [Fact]
    public async Task AFailureTheHelperReports_KeepsTheHelper()
    {
        var helpers = new FakeHelpers { Setup = h => h.Answer = r => FakeHelper.Fail(r, "too_much_text") };
        using var ai = helpers.Client();
        Assert.Equal("too_much_text", (await Assert.ThrowsAsync<DescriptionException>(() => ai.DescribeAsync(Jpeg, "accessible", Token))).Code);
        Assert.False(helpers.Started[0].Killed);
        Assert.Single(helpers.Started[0].Requests); // too much text isn't tried again smaller
    }

    [Fact]
    public async Task WarmingUp_DescribesAndReadsATinyPicture_ThroughTheSameHelper()
    {
        var helpers = new FakeHelpers();
        using var ai = helpers.Client();
        await ai.WarmUpAsync(Token);
        var helper = Assert.Single(helpers.Started);
        Assert.Equal(2, helper.Requests.Count);
        Assert.Equal("brief", (string?)helper.Requests[0]["kind"]);
        Assert.Equal("read", (string?)helper.Requests[1]["op"]);
        // The helper stays, warm, for the first real picture.
        Assert.False(helper.Killed);
        Assert.Equal("A description.", await ai.DescribeAsync(Jpeg, "brief", Token));
        Assert.Single(helpers.Started);
    }

    [Fact]
    public async Task WarmingUp_DoesntMindTheModelsVerdictOnTheBlankPicture_OrAPCWithoutTheRecognizer()
    {
        var helpers = new FakeHelpers
        {
            Setup = h => h.Answer = r => (string?)r["op"] == "read" ? FakeHelper.Fail(r, "not_supported") : FakeHelper.Fail(r, "content_filtered"),
        };
        using var ai = helpers.Client();
        await ai.WarmUpAsync(Token);
        Assert.Equal(2, helpers.Started[0].Requests.Count);
    }

    [Fact]
    public async Task AWarmUpThatFails_IsTriedOnce_NotAtEverySize()
    {
        var helpers = new FakeHelpers { Setup = h => h.Answer = r => FakeHelper.Fail(r, "internal_error") };
        using var ai = helpers.Client();
        Assert.Equal("internal_error", (await Assert.ThrowsAsync<DescriptionException>(() => ai.WarmUpAsync(Token))).Code);
        Assert.Single(helpers.Started[0].Requests);
    }

    [Fact]
    public async Task AWarmUpThatHangs_GivesUpAtItsOwnLimit_AndFreesTheHelper()
    {
        var was = WindowsAI.WarmUpTimeout;
        WindowsAI.WarmUpTimeout = TimeSpan.FromMilliseconds(200);
        try
        {
            var helpers = new FakeHelpers { Setup = h => h.Hold = new TaskCompletionSource() };
            using var ai = helpers.Client(TimeSpan.FromMinutes(3));
            // Said, not taken for closing, so it's logged.
            var ex = await Assert.ThrowsAsync<DescriptionException>(() => ai.WarmUpAsync(Token));
            Assert.Equal("timeout", ex.Code);
            Assert.Equal(WindowsAI.NotRespondingMessage, ex.Message);
            Assert.True(helpers.Started[0].Killed);
            // The next request gets a fresh helper at once.
            helpers.Setup = null;
            Assert.Equal(["Line one", "Line two"], await ai.ReadTextAsync([1], Token));
        }
        finally
        {
            WindowsAI.WarmUpTimeout = was;
        }
    }

    [Fact]
    public async Task WarmingUp_WhenWindowsAIIsntThere_SaysSo()
    {
        var helpers = new FakeHelpers { StartFailure = new DescriptionException("not_installed", "No helper.") };
        using var ai = helpers.Client();
        Assert.Equal("not_installed", (await Assert.ThrowsAsync<DescriptionException>(() => ai.WarmUpAsync(Token))).Code);
    }

    [Theory]
    [InlineData("Ready", true)]
    [InlineData("NotSupportedOnCurrentSystem", true)]
    [InlineData("NotReady", false)]
    [InlineData("DisabledByUser", false)]
    [InlineData("Error", false)]
    public void OnlyStatesThatCantChange_AreRememberedForTheSession(string state, bool final) =>
        Assert.Equal(final, WindowsAI.IsFinal(state));

    [Theory]
    [InlineData("Ready", "Phi Silica, Windows' language model, is on this PC. It isn't supported in Quick Describe yet.")]
    [InlineData("NotSupportedOnCurrentSystem", "Phi Silica, Windows' language model, isn't supported in Quick Describe yet.")]
    public void CheckWindowsAI_DoesntOfferPhiSilica_AsIfQuickDescribeCouldUseIt(string state, string line) =>
        Assert.Equal(line, QuickDescribe.Views.MainWindow.LanguageModelLine(state));
}

/// <summary>Describing and reading together, with a fake helper: one half failing never loses the other.</summary>
[Collection("Text recognition")]
public class DescribingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static Task<AIReadiness> Ready(CancellationToken _) => Task.FromResult(new AIReadiness(true, "Ready", null));
    private static Task<AIReadiness> NotSupported(CancellationToken _) =>
        Task.FromResult(new AIReadiness(true, "NotSupportedOnCurrentSystem", "Needs a Copilot+ PC."));

    public DescribingTests() => TextRecognition.ForgetUnavailable();

    [Fact]
    public async Task BothHalves_ComeBack()
    {
        var helpers = new FakeHelpers();
        using var ai = helpers.Client();
        var result = await Describing.RunAsync(Drawn.Solid(64, 64, 0, 0, 200), "brief", ai, Token, Ready);
        Assert.Equal("A description.", result.Description);
        Assert.Equal(["Line one", "Line two"], result.Lines);
    }

    [Fact]
    public async Task TooMuchText_StillGivesTheText()
    {
        var helpers = new FakeHelpers { Setup = h => h.Answer = r => (string?)r["op"] == "read" ? FakeHelper.Ok(r) : FakeHelper.Fail(r, "too_much_text") };
        using var ai = helpers.Client();
        var result = await Describing.RunAsync(Drawn.Solid(64, 64, 0, 0, 200), "accessible", ai, Token, Ready);
        Assert.Null(result.Description);
        Assert.Equal("too_much_text", result.DescriptionCode);
        Assert.Equal(["Line one", "Line two"], result.Lines);
    }

    [Fact]
    public async Task NoWindowsAI_StillReadsTheText_WithTheBuiltInOcr()
    {
        if (Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages() is null) Assert.Skip("No OCR language on this PC.");
        var helpers = new FakeHelpers { StartFailure = new DescriptionException("not_installed", "The helper didn't start.") };
        using var ai = helpers.Client();
        var result = await Describing.RunAsync(Drawn.Text(600, 120, 28, "Save Cancel Help"), "accessible", ai, Token, NotSupported);
        Assert.Equal("unavailable", result.DescriptionCode);
        Assert.Equal(["Save Cancel Help"], result.Lines);
    }

    [Fact]
    public async Task AModelStillGettingReady_IsntAskedToDescribe_AndTheTextStillComes()
    {
        var helpers = new FakeHelpers();
        using var ai = helpers.Client();
        var result = await Describing.RunAsync(Drawn.Solid(64, 64, 0, 0, 200), "brief", ai, Token,
            _ => Task.FromResult(new AIReadiness(true, "NotReady", null)));
        Assert.Equal("not_ready", result.DescriptionCode);
        Assert.Equal(Describing.NotReadyMessage, result.DescriptionProblem);
        Assert.Equal(["Line one", "Line two"], result.Lines);
        Assert.Equal(["read"], helpers.Started[0].Requests.Select(r => (string?)r["op"]));
    }

    [Fact]
    public async Task ACheckThatThrows_IsAFailedDescription_NotALostResult()
    {
        var helpers = new FakeHelpers();
        using var ai = helpers.Client();
        var result = await Describing.RunAsync(Drawn.Solid(64, 64, 0, 0, 200), "brief", ai, Token,
            _ => throw new InvalidOperationException("The helper went away."));
        Assert.Equal("error", result.DescriptionCode);
        Assert.Contains("The helper went away.", result.DescriptionProblem);
        Assert.Equal(["Line one", "Line two"], result.Lines);
    }

    [Fact]
    public async Task GivingUp_ThrowsRatherThanGivingAResult()
    {
        var helpers = new FakeHelpers { Setup = h => h.Hold = new TaskCompletionSource() };
        using var ai = helpers.Client();
        using var cancel = new CancellationTokenSource();
        var running = Describing.RunAsync(Drawn.Solid(64, 64, 0, 0, 200), "brief", ai, cancel.Token, Ready);
        await Task.Delay(100, Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }
}

public class RequestsTests
{
    [Fact]
    public void ANewRequest_GivesUpTheOneBefore()
    {
        var requests = new Requests();
        var first = requests.Start();
        var second = requests.Start();
        Assert.True(first.Token.IsCancellationRequested);
        Assert.False(requests.IsCurrent(first));
        Assert.True(requests.IsCurrent(second));
        Assert.True(requests.IsRunning);
    }

    [Fact]
    public void Cancel_GivesUpTheRequest_AndSaysWhetherThereWasOne()
    {
        var requests = new Requests();
        Assert.False(requests.Cancel());
        var request = requests.Start();
        Assert.True(requests.Cancel());
        Assert.True(request.Token.IsCancellationRequested);
        Assert.False(requests.IsCurrent(request));
        Assert.False(requests.IsRunning);
        Assert.False(requests.Cancel());
    }

    [Fact]
    public void AFinishedRequest_IsNoLongerRunning_AndCancellingDoesNothing()
    {
        var requests = new Requests();
        var request = requests.Start();
        requests.Finish(request);
        Assert.False(requests.IsRunning);
        Assert.False(requests.Cancel());
        Assert.False(request.Token.IsCancellationRequested);
    }

    [Fact]
    public void FinishingAnOldRequest_LeavesTheNewOneRunning()
    {
        var requests = new Requests();
        var old = requests.Start();
        var current = requests.Start();
        requests.Finish(old);
        Assert.True(requests.IsRunning);
        Assert.True(requests.IsCurrent(current));
    }
}
