using EQClassic.Shared.Login;
using EQClassic.Shared.Protocol;

namespace EQClassic.Tests.Protocol;

public class MessageCodecTests
{
    public static TheoryData<IMessage> Messages => new()
    {
        new LoginRequest("test", "pa55word"),
        LoginResponse.Success(16),
        LoginResponse.Failure(LoginResult.Banned),
        new ServerListRequest(),
        new ServerListResponse([new WorldServerInfo(1, "EverQuest Classic", "192.168.1.2", 9000, 12, WorldStatus.Up),
                                new WorldServerInfo(2, "Test", "10.0.0.1", 9001, 0, WorldStatus.Locked)]),
        new PlayRequest(1),
        new PlayResponse(true, "", "abcDEF123456789", "192.168.1.2", 9000),
        PlayResponse.Refused(LoginMessages.WorldDown),
    };

    [Theory]
    [MemberData(nameof(Messages))]
    public void Messages_round_trip(IMessage message)
    {
        Assert.Equal(message, MessageCodec.Decode(MessageCodec.Encode(message)));
    }

    [Fact]
    public void Type_byte_comes_first()
    {
        Assert.Equal((byte)MessageType.PlayRequest, MessageCodec.Encode(new PlayRequest(3))[0]);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 99 })]
    [InlineData(new byte[] { (byte)MessageType.PlayRequest, 1 })]
    [InlineData(new byte[] { (byte)MessageType.PlayRequest, 1, 0, 0, 0, 7 })]
    public void Unknown_truncated_or_padded_input_is_rejected(byte[] data)
    {
        Assert.Throws<MessageFormatException>(() => MessageCodec.Decode(data));
    }

    [Fact]
    public void Login_request_never_prints_the_password()
    {
        Assert.DoesNotContain("pa55word", new LoginRequest("test", "pa55word").ToString());
    }
}
