using Valour.Server.Utilities;

namespace Valour.Tests.Server;

public class SecurityHeadersTests
{
    [Fact]
    public void InlineScriptHashes_SkipExternalScriptsAndNormalizeNewlines()
    {
        const string lf = "<script src=\"a.js\"></script>\n<script>\n    run();\n</script>";
        const string crlf = "<script src=\"a.js\"></script>\r\n<script>\r\n    run();\r\n</script>";

        var fromLf = SecurityHeaders.ComputeInlineScriptHashes(lf);
        var fromCrlf = SecurityHeaders.ComputeInlineScriptHashes(crlf);

        var hash = Assert.Single(fromLf);
        Assert.Equal(fromLf, fromCrlf);
        // sha256 of "\n    run();\n", the text a browser hashes for this element.
        var expected = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData("\n    run();\n"u8.ToArray()));
        Assert.Equal("sha256-" + expected, hash);
    }

    [Fact]
    public void AppPolicy_AllowsOnlyHashedInlineScripts()
    {
        var policy = SecurityHeaders.BuildAppPolicy(["sha256-abc", "sha256-def"], isDevelopment: false);

        var scriptSrc = Directive(policy, "script-src");
        Assert.StartsWith("script-src 'self' 'wasm-unsafe-eval' 'sha256-abc' 'sha256-def'", scriptSrc);
        Assert.DoesNotContain("'unsafe-inline'", scriptSrc);
        Assert.DoesNotContain("'unsafe-eval'", scriptSrc);
        Assert.DoesNotContain("'unsafe-hashes'", scriptSrc);
        Assert.Contains("object-src 'none'", policy);
        Assert.Contains("base-uri 'self'", policy);
        Assert.Contains("frame-ancestors 'none'", policy);
        Assert.Contains("connect-src 'self' https: wss:", policy);
        Assert.DoesNotContain(" http:", policy);
        Assert.DoesNotContain(" ws:", policy);
    }

    [Fact]
    public void AppPolicy_AllowsPlainConnectionsOnlyInDevelopment()
    {
        var policy = SecurityHeaders.BuildAppPolicy(["sha256-abc"], isDevelopment: true);

        Assert.Contains("connect-src 'self' https: wss: http: ws:", policy);
        Assert.Equal(
            Directive(SecurityHeaders.BuildAppPolicy(["sha256-abc"], isDevelopment: false), "script-src"),
            Directive(policy, "script-src"));
    }

    [Fact]
    public void PublicPagePolicy_IncludesRequestNonce()
    {
        var policy = SecurityHeaders.BuildPublicPagePolicy("abc123");

        var scriptSrc = Directive(policy, "script-src");
        Assert.Contains("'nonce-abc123'", scriptSrc);
        Assert.DoesNotContain("'unsafe-inline'", scriptSrc);
        Assert.Contains("frame-ancestors 'none'", policy);
    }

    private static string Directive(string policy, string name) =>
        policy.Split("; ").Single(d => d.StartsWith(name + " "));
}
