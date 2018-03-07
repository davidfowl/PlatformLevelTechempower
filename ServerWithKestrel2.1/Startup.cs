using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace ServerWithKestrel21
{
    public class Startup
    {
        private static AsciiString _helloWorldPayload = "Hello, World!";
        private static readonly int _helloWorldLength = _helloWorldPayload.Length;
        private static readonly string _helloWorldLengthValue = _helloWorldPayload.Length.ToString();

        public void Configure(IApplicationBuilder app)
        {
            // This is the ASP.NET Core application running on Kestrel
            app.Run(context =>
            {
                context.Response.StatusCode = 200;
                context.Response.ContentType = "text/plain";
                context.Response.ContentLength = _helloWorldLength;
                return context.Response.Body.WriteAsync(_helloWorldPayload, 0, _helloWorldLength);
            });
        }
    }
}
