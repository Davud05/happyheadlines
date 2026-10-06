using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Website.Pages;

public class ArticleModel(ArticleClient articles, CommentClient comments, ILogger<ArticleModel> logger) : PageModel
{
    public ArticleDto? Article { get; private set; }
    public List<CommentDto> Comments { get; private set; } = [];
    public string? CommentsError { get; private set; }

    [TempData] public string? Message { get; set; }

    [BindProperty, Required, StringLength(200)] public string Author { get; set; } = "";
    [BindProperty, Required, StringLength(4000)] public string Content { get; set; } = "";

    public async Task<IActionResult> OnGetAsync(string continent, Guid id, CancellationToken ct)
    {
        try
        {
            Article = await articles.GetAsync(continent, id, ct);
        }
        catch (Exception ex) when (Downstream.IsUnavailable(ex))
        {
            logger.LogWarning("ArticleService unavailable: {Reason}", ex.Message);
            return StatusCode(503);
        }
        if (Article is null) return NotFound();

        // Comments live in their own swimlane: if they fail, the article is still shown.
        try
        {
            Comments = await comments.ForArticleAsync(id, ct);
        }
        catch (Exception ex) when (Downstream.IsUnavailable(ex))
        {
            logger.LogWarning("CommentService unavailable: {Reason}", ex.Message);
            CommentsError = "Comments are temporarily unavailable.";
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string continent, Guid id, CancellationToken ct)
    {
        if (!ModelState.IsValid) return await OnGetAsync(continent, id, ct);

        try
        {
            var visible = await comments.PostAsync(id, Author, Content, ct);
            Message = visible ? "Thanks for your comment!" : "Thanks! Your comment will appear once it has been reviewed.";
        }
        catch (Exception ex) when (Downstream.IsUnavailable(ex))
        {
            logger.LogWarning("Posting comment failed: {Reason}", ex.Message);
            Message = "Your comment could not be posted right now. Please try again later.";
        }
        return RedirectToPage(new { continent, id });
    }
}
