using Valour.Client.Components.Menus.Modals.Users.Edit;

namespace Valour.Tests.Client;

public class ProfileImageDataUrlTests
{
    [Fact]
    public void TryDecodeImageDataUrl_UsesActualCanvasMimeType()
    {
        var expected = new byte[] { 1, 2, 3, 4 };
        var dataUrl = $"data:image/png;base64,{Convert.ToBase64String(expected)}";

        var success = EditProfileComponent.TryDecodeImageDataUrl(
            dataUrl,
            out var bytes,
            out var contentType);

        Assert.True(success);
        Assert.Equal("image/png", contentType);
        Assert.Equal(expected, bytes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.test/image.png")]
    [InlineData("data:text/plain;base64,SGVsbG8=")]
    [InlineData("data:image/png,not-base64")]
    [InlineData("data:image/png;base64,not-base64")]
    public void TryDecodeImageDataUrl_RejectsInvalidInput(string dataUrl)
    {
        Assert.False(EditProfileComponent.TryDecodeImageDataUrl(
            dataUrl,
            out _,
            out _));
    }
}
