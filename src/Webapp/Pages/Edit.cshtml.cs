using System.ComponentModel.DataAnnotations;
using HappyHeadlines.Shared.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Webapp.Pages;

public class EditModel(DraftClient drafts) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }

    [BindProperty, Required] public string Title { get; set; } = "";
    [BindProperty] public string Content { get; set; } = "";
    [BindProperty, Required] public string Author { get; set; } = "";
    [BindProperty] public string Continent { get; set; } = Continents.Global;

    public IReadOnlyList<string> AllContinents => Continents.All;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Id is not { } id) return Page();

        var draft = await drafts.GetAsync(id, ct);
        if (draft is null) return NotFound();

        (Title, Content, Author, Continent) = (draft.Title, draft.Content, draft.Author, draft.Continent);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) return Page();

        await drafts.SaveAsync(Id, new DraftInput(Title, Content ?? "", Author, Continent), ct);
        TempData["Message"] = $"\"{Title}\" was saved.";
        return RedirectToPage("/Index");
    }
}
