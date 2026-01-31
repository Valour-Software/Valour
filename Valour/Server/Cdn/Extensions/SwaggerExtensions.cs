using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Valour.Server.Cdn.Extensions
{
    /// Add extra parameters for uploading files in swagger.
    /// </summary>
    public class FileUploadOperation : IOperationFilter
    {
        /// <summary>
        /// Applies the specified operation.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="context">The context.</param>
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {

            var isFileUploadOperation =
                context.ApiDescription.ActionDescriptor.EndpointMetadata.Any(a => a is FileContentType);

            if (!isFileUploadOperation) return;

            //operation.Parameters.Clear();

            var uploadFileMediaType = new OpenApiMediaType()
            {
                Schema = new OpenApiSchema()
                {
                    Type = JsonSchemaType.Object,
                    Properties =
                {
                    ["uploadedFile"] = new OpenApiSchema()
                    {
                        Description = "Upload File",
                        Type = JsonSchemaType.Object,
                        Format = "formData"
                    }
                },
                    Required = new HashSet<string>() { "uploadedFile" }
                }
            };

            operation.RequestBody = new OpenApiRequestBody
            {
                Content = { ["multipart/form-data"] = uploadFileMediaType }
            };
        }

        /// <summary>
        /// Indicates swashbuckle should consider the parameter as a file upload
        /// </summary>
        [AttributeUsage(AttributeTargets.Method)]
        public class FileContentType : Attribute
        {

        }
    }
}

