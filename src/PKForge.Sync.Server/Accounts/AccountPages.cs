using System.Net;
using PKForge.Sync.Server.Http;

namespace PKForge.Sync.Server.Accounts;

/// <summary>The two pages email links open in a browser.</summary>
public static class AccountPages
{
    public static void MapAccountPages(this IEndpointRouteBuilder app)
    {
        app.MapGet("/verify-email", async (string? token, AccountService accounts, CancellationToken ct) =>
            Page("Email confirmation", Text(await accounts.VerifyEmailAsync(token, ct)
                ? "Your email is confirmed. You can go back to PKForge."
                : "This link is invalid or has expired. Ask PKForge to send a new one.")));

        app.MapGet("/reset-password", (string? token) => Page("Reset password", ResetForm(token ?? "")));

        app.MapPost("/reset-password", async (HttpContext http, AccountService accounts, CancellationToken ct) =>
        {
            var form = await http.Request.ReadFormAsync(ct);
            string token = form["token"].ToString(), password = form["password"].ToString(), confirm = form["confirm"].ToString();
            if (password != confirm) return Page("Reset password", Text("The passwords don't match.") + ResetForm(token));
            try
            {
                return Page("Reset password", Text(await accounts.ConfirmPasswordResetAsync(token, password, ct)
                    ? "Your password was changed. Sign in to PKForge with the new password."
                    : "This link is invalid or has expired. Ask PKForge for a new one."));
            }
            catch (ApiException e)
            {
                return Page("Reset password", Text(e.Message) + ResetForm(token));
            }
        });
    }

    private static string Text(string message) => $"<p>{WebUtility.HtmlEncode(message)}</p>";

    private static string ResetForm(string token) => $"""
        <form method="post" action="/reset-password">
          <input type="hidden" name="token" value="{WebUtility.HtmlEncode(token)}">
          <label>New password <input type="password" name="password" minlength="{AccountService.MinPasswordLength}" required></label>
          <label>Repeat it <input type="password" name="confirm" minlength="{AccountService.MinPasswordLength}" required></label>
          <button type="submit">Change password</button>
        </form>
        """;

    private static IResult Page(string title, string body) => Results.Content($$"""
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{WebUtility.HtmlEncode(title)}} · PKForge Sync</title>
        <style>body{font-family:system-ui,sans-serif;max-width:28rem;margin:3rem auto;padding:0 1rem;line-height:1.5}
        label{display:block;margin:.75rem 0}input{display:block;width:100%;padding:.4rem;margin-top:.25rem}
        button{padding:.5rem 1rem}</style></head>
        <body><h1>{{WebUtility.HtmlEncode(title)}}</h1>{{body}}</body></html>
        """, "text/html; charset=utf-8");
}
