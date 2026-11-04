using Microsoft.Playwright;
using Restaurant.Aqa.Configuration;
using static Microsoft.Playwright.Assertions;

namespace Restaurant.Aqa.Pages;

public sealed class LoginPage(IPage page)
{
    public async Task SignInAsync(TestAccount account)
    {
        // Login requests contain passwords and tokens. Start the retained trace only
        // after authentication, so no credential request bodies are saved in artifacts.
        await page.Context.Tracing.StopAsync();
        var stage = "opening the login page";
        try
        {
            await page.GotoAsync("login");
            var form = page.Locator("app-login-form");
            stage = "entering the email";
            await form.GetByLabel("Email", new() { Exact = true }).FillAsync(account.Email);
            stage = "locating the password input";
            // The wrapping label includes other controls and whitespace. Use the
            // stable Angular form-control attribute on the native input itself.
            var password = form.Locator("input[formcontrolname='password']");
            await Expect(password).ToBeVisibleAsync();
            stage = "entering the password";
            await password.FillAsync(account.Password);
            stage = "submitting and waiting for the account profile";
            await page.RunAndWaitForResponseAsync(
                () => form.GetByRole(AriaRole.Button, new() { Name = "Sign In", Exact = true }).ClickAsync(),
                response => new Uri(response.Url).AbsolutePath.EndsWith("/me") && response.Status == 200);
            stage = "waiting for the login form to close";
            await Expect(form).Not.ToBeVisibleAsync();
        }
        catch
        {
            // Playwright action logs can include FillAsync values. Do not propagate
            // credential-entry diagnostics into public CI logs or reports.
            throw new InvalidOperationException($"UI sign-in failed while {stage}. Check the dedicated account and deployed login page.");
        }
        await page.Context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });
    }
}
