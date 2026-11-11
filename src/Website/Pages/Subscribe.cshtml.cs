using System.ComponentModel.DataAnnotations;
using HappyHeadlines.Shared.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Website.Pages;

public class SubscribeModel(SubscriberClient subscribers, IFeatureToggles toggles, ILogger<SubscribeModel> logger) : PageModel
{
    [BindProperty, Required, EmailAddress, StringLength(320)] public string Email { get; set; } = "";

    [TempData] public string? Message { get; set; }
    [TempData] public bool Succeeded { get; set; }

    private bool Released => toggles.IsEnabled(FeatureFlags.SubscriberService);

    public IActionResult OnGet() => Released ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!Released) return NotFound();
        if (!ModelState.IsValid) return Page();

        SubscribeOutcome outcome;
        try
        {
            outcome = await subscribers.SubscribeAsync(Email, ct);
        }
        catch (Exception ex) when (Downstream.IsUnavailable(ex))
        {
            logger.LogWarning("SubscriberService unavailable: {Reason}", ex.Message);
            outcome = SubscribeOutcome.Unavailable;
        }

        (Succeeded, Message) = outcome switch
        {
            SubscribeOutcome.Subscribed => (true, "You're subscribed! Good news is on its way."),
            SubscribeOutcome.AlreadySubscribed => (true, "You're already subscribed. Thanks for staying positive!"),
            SubscribeOutcome.InvalidEmail => (false, "That doesn't look like a valid email address."),
            _ => (false, "We can't sign you up right now. Please try again in a moment."),
        };
        return RedirectToPage();
    }
}
