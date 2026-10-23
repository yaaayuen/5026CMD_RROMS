using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Admin.Pages.DietaryTags;

/// <summary>
/// Administrator adds dietary tags (YY-10). Owner: YY.
/// Protected by the Admin area folder policy (YY-3), so only administrators can open it.
/// </summary>
public class IndexModel : PageModel
{
    private readonly IDietaryTagService _dietaryTags;

    //Receives the dietary tag service from dependency injection
    public IndexModel(IDietaryTagService dietaryTags)
    {
        _dietaryTags = dietaryTags;
    }

    //The tags that already exist, in display order
    public IReadOnlyList<DietaryTag> Tags { get; private set; } = new List<DietaryTag>();

    //The name typed into the "Add a tag" form
    [BindProperty]
    [Required(ErrorMessage = "Enter a name for the dietary tag.")]
    [StringLength(DietaryTag.NameMaxLength, ErrorMessage = "The name can be at most {1} characters.")]
    [Display(Name = "New tag name")]
    public string NewName { get; set; } = string.Empty;

    //Shows the list and the empty form
    public async Task OnGetAsync()
    {
        Tags = await _dietaryTags.GetAllAsync();
    }

    //Validates the name and adds the tag
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            Tags = await _dietaryTags.GetAllAsync();
            return Page();
        }

        ServiceResult result = await _dietaryTags.AddAsync(NewName);
        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(nameof(NewName), error);
            }

            Tags = await _dietaryTags.GetAllAsync();
            return Page();
        }

        TempData["StatusMessage"] = $"Dietary tag \"{NewName.Trim()}\" added. Customers can now choose it on their profile.";
        return RedirectToPage();
    }
}