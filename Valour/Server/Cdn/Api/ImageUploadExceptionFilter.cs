using SixLabors.ImageSharp;

namespace Valour.Server.Cdn.Api;

public sealed class ImageUploadExceptionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (Exception e) when (e is UnknownImageFormatException or ImageFormatException)
        {
            return Results.BadRequest("The image is damaged or has an unsupported format.");
        }
    }
}
