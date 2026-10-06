using System.Net;
using System.Text.RegularExpressions;

namespace Rroms.Tests.Infrastructure;

/// <summary>
/// Submits Razor Pages forms from tests the way a browser would, including the antiforgery token.
/// Every POST in the app must carry the hidden "__RequestVerificationToken" field from a page we served,
/// plus the matching antiforgery cookie. The helper loads the page first, reads the token from the HTML,
/// then posts it back. The HttpClient keeps the cookie automatically.
/// /// </summary>
public static class FormHelper
{
    //Name of the hidden field Razor Pages adds to every POST form
    public const string TokenFieldName = "__RequestVerificationToken";

    private static readonly Regex TokenPattern = new Regex(
        "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    //Loads a page and returns the antiforgery token found in its HTML
    public static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string pageUrl)
    {
        string html = await client.GetStringAsync(pageUrl);
        Match match = TokenPattern.Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException($"No antiforgery token found on {pageUrl}.");
        }

        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    /// <summary>
    /// Loads <paramref name="formPageUrl"/> to get a token, then posts <paramref name="fields"/>
    /// to <paramref name="postUrl"/>
    /// </summary>
    public static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string formPageUrl, string postUrl, IDictionary<string, string> fields)
    {
        string token = await GetAntiforgeryTokenAsync(client, formPageUrl);

        var formFields = new Dictionary<string, string>(fields)
        {
            [TokenFieldName] = token
        };

        return await client.PostAsync(postUrl, new FormUrlEncodedContent(formFields));
    }

    //Signs in through the real login form and returns the response (normally a redirect)
    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password, string? returnUrl = null)
    {
        string url = returnUrl == null
            ? "/Account/Login"
            : $"/Account/Login?ReturnUrl={Uri.EscapeDataString(returnUrl)}";

        var fields = new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password
        };

        return PostFormAsync(client, url, url, fields);
    }
}