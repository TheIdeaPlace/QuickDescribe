using System.Text.Json.Nodes;
using QuickDescribeAI;
using Xunit;

namespace QuickDescribeAI.Tests;

/// <summary>The "read" operation: the text in a picture, line by line, from Windows AI's text recognizer.</summary>
public class ReadTests
{
    private static readonly string Png = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47]);

    private static string Req(int id, string? op, string? kind = null)
    {
        var o = new JsonObject { ["id"] = id, ["mime"] = "image/png", ["image"] = Png };
        if (op is not null) o["op"] = op;
        if (kind is not null) o["kind"] = kind;
        return o.ToJsonString();
    }

    [Fact]
    public void ARequest_IsADescription_UnlessItSaysRead()
    {
        Assert.Equal(Protocol.Describe, Protocol.ParseRequest(Req(1, null)).Op);
        Assert.Equal(Protocol.Describe, Protocol.ParseRequest(Req(1, "describe")).Op);
        Assert.Equal(Protocol.Read, Protocol.ParseRequest(Req(1, " READ ")).Op);
    }

    [Fact]
    public void ARead_IgnoresAnyKind_EvenOneThatIsntAKind()
    {
        var r = Protocol.ParseRequest(Req(4, "read", kind: "poetic"));
        Assert.Equal((4, Protocol.Read, "image/png"), (r.Id, r.Op, r.Mime));
    }

    [Fact]
    public void AnUnknownOperation_IsABadRequest_ThatKeepsItsId()
    {
        var ex = Assert.Throws<ProtocolException>(() => Protocol.ParseRequest(Req(9, "ask")));
        Assert.Equal((9, Codes.BadRequest), (ex.Id, ex.Code));
        Assert.Contains("describe and read", ex.Message);
    }

    [Fact]
    public void ARead_StillChecksThePicture()
    {
        var ex = Assert.Throws<ProtocolException>(() =>
            Protocol.ParseRequest(new JsonObject { ["id"] = 2, ["op"] = "read", ["mime"] = "image/webp", ["image"] = Png }.ToJsonString()));
        Assert.Equal(Codes.UnsupportedFormat, ex.Code);
    }

    [Fact]
    public void AReadsAnswer_HasItsLines()
    {
        var o = JsonNode.Parse(Protocol.ReadSuccess(3, ["File", "Don't save"], 0.4321))!.AsObject();
        Assert.Equal(3, (int)o["id"]!);
        Assert.True((bool)o["ok"]!);
        Assert.Equal(["File", "Don't save"], o["lines"]!.AsArray().Select(l => (string)l!));
        Assert.Equal(0.43, (double)o["seconds"]!);
        Assert.Contains("Don't", Protocol.ReadSuccess(3, ["Don't"], 0)); // apostrophes stay readable
    }

    [Fact]
    public async Task TheServer_SendsReadsToTheReader_AndDescriptionsToTheDescriber()
    {
        var output = new StringWriter();
        await Server.RunAsync(new StringReader($"{Req(1, "read")}\n{Req(2, null)}\n"), output,
            r => Task.FromResult($"described {r.Id}"), null,
            r => Task.FromResult<IReadOnlyList<string>>([$"read {r.Id}", "second line"]));
        var answers = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonNode.Parse(l)!.AsObject()).ToList();
        Assert.Equal(["read 1", "second line"], answers[0]["lines"]!.AsArray().Select(l => (string)l!));
        Assert.Null(answers[0]["text"]);
        Assert.Equal("described 2", (string)answers[1]["text"]!);
    }

    [Fact]
    public async Task AServerWithoutAReader_RefusesReads_AndCarriesOn()
    {
        var output = new StringWriter();
        await Server.RunAsync(new StringReader($"{Req(1, "read")}\n{Req(2, null)}\n"), output, r => Task.FromResult("fine"));
        var answers = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonNode.Parse(l)!.AsObject()).ToList();
        Assert.Equal((1, Codes.BadRequest), ((int)answers[0]["id"]!, (string)answers[0]["code"]!));
        Assert.Equal("fine", (string)answers[1]["text"]!);
    }

    [Fact]
    public async Task AReadersFailure_IsAnsweredWithItsCode()
    {
        var output = new StringWriter();
        var resets = 0;
        await Server.RunAsync(new StringReader($"{Req(6, "read")}\n"), output, r => Task.FromResult("x"), () => resets++,
            r => throw new ProtocolException(r.Id, Codes.NotSupported, "No recognizer here."));
        var answer = JsonNode.Parse(output.ToString())!.AsObject();
        Assert.Equal((6, Codes.NotSupported), ((int)answer["id"]!, (string)answer["code"]!));
        Assert.Equal(0, resets);
    }
}
