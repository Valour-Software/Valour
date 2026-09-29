using System.Text;
using Valour.Server.Cdn;

namespace Valour.Tests.Cdn;

public class SitePreviewFetcherTests
{
    [Fact]
    public void Clean_DecodesEntitiesAndCollapsesWhitespace()
    {
        Assert.Equal("Tom & Jerry's show", SitePreviewFetcher.Clean("  Tom &amp; Jerry&#39;s\n\t  show ", 100));
    }

    [Fact]
    public void Clean_RemovesDirectionOverridesAndInvisibleCharacters()
    {
        // A right-to-left override can make "exe.txt" read as "txt.exe".
        Assert.Equal("invoicetxt.exe", SitePreviewFetcher.Clean("invoice‮txt.exe​﻿", 100));
    }

    [Fact]
    public void Clean_KeepsEmojiJoinersAndShortensByVisibleCharacters()
    {
        var family = "\U0001F468‍\U0001F469‍\U0001F467";
        Assert.Equal(family + " family", SitePreviewFetcher.Clean(family + " family", 100));

        var shortened = SitePreviewFetcher.Clean(family + family + family, 2);
        Assert.Equal(family + "…", shortened);
    }

    [Fact]
    public void Clean_ReturnsNullForBlankText()
    {
        Assert.Null(SitePreviewFetcher.Clean(" ​ \t ", 100));
    }

    [Fact]
    public void ReadTags_ReadsOpenGraphTwitterAndTitleInAnyAttributeOrder()
    {
        var tags = SitePreviewFetcher.ReadTags("""
            <title>Fallback title</title>
            <meta content="From content-first" property="og:title">
            <meta property='og:image' content='/cover.png'>
            <meta name=twitter:card content=summary_large_image>
            <meta property="og:title" content="Second title is ignored">
            <link rel="icon" href="/favicon.png"><link rel="apple-touch-icon" href="/touch.png">
            """);

        Assert.Equal("From content-first", tags.First("og:title"));
        Assert.Equal("/cover.png", tags.First("og:image"));
        Assert.Equal("summary_large_image", tags.First("twitter:card"));
        Assert.Equal("Fallback title", tags.Title);
        Assert.Equal(["/touch.png", "/favicon.png"], tags.IconCandidates);
    }

    [Fact]
    public void ReadTags_StaysFastOnHostileMarkup()
    {
        // Unclosed tags repeated across the whole allowed head size made the
        // old backtracking patterns take quadratic time.
        var hostile = string.Concat(Enumerable.Repeat("<title<meta property=\"og:", 10_000));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        SitePreviewFetcher.ReadTags(hostile);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Took {watch.Elapsed}");
    }

    [Fact]
    public void Decode_UsesTheCharsetTheMarkupDeclares()
    {
        var encoding = CodePagesEncodingProvider.Instance.GetEncoding("windows-1251")!;
        var bytes = encoding.GetBytes("<meta charset=\"windows-1251\"><title>Привет</title>");

        Assert.Contains("Привет", SitePreviewFetcher.Decode(bytes, null));
    }
}
