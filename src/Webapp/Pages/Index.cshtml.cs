using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Webapp.Pages;

public class IndexModel(DraftClient drafts, PublisherClient publisher, ILogger<IndexModel> logger) : PageModel
{
    public List<DraftDto> Drafts { get; private set; } = [];

    [TempData] public string? Message { get; set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        try
        {
            Drafts = await drafts.ListAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or Polly.ExecutionRejectedException)
        {
            logger.LogWarning("DraftService unavailable: {Reason}", ex.Message);
            Error = "Drafts are temporarily unavailable. Please try again in a moment.";
        }
    }

    public async Task<IActionResult> OnPostPublishAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var draft = await drafts.GetAsync(id, ct);
            if (draft is null) return NotFound();

            await publisher.PublishAsync(draft, ct);
            await drafts.DeleteAsync(id, ct);
            Message = $"\"{draft.Title}\" was published.";
        }
        catch (Exception ex) when (ex is HttpRequestException or Polly.ExecutionRejectedException)
        {
            logger.LogWarning("Publishing draft {DraftId} failed: {Reason}", id, ex.Message);
            Message = "Publishing failed. The draft was kept so you can try again.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken ct)
    {
        await drafts.DeleteAsync(id, ct);
        Message = "Draft deleted.";
        return RedirectToPage();
    }
}
