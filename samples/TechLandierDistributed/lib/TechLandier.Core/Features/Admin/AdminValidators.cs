using Microsoft.EntityFrameworkCore;
using TechLandier.Data;

namespace TechLandier.Features;

public static class AdminValidators
{
    /// First problem with a product form, as (field, message), or null when it can be saved.
    public static async Task<(string Field, string Message)?> ProductErrorAsync(AppDb db, ProductInput input, CancellationToken ct)
    {
        if (input.Name?.Trim() is not { Length: >= 3 and <= 80 }) return ("name", "Give the product a name of 3 to 80 characters.");
        if (input.Brand?.Trim() is not { Length: >= 1 and <= 80 }) return ("brand", "Add the brand.");
        if (!await db.Categories.AnyAsync(c => c.Id == input.CategoryId, ct)) return ("categoryId", "Pick a category.");
        if (input.Price is not > 0) return ("price", "Price must be greater than zero.");
        if (input.DiscountPercent is < 0 or > 70) return ("discountPercent", "Discounts run from 0 to 70%.");
        if (input.Summary?.Length > 280) return ("summary", "Keep the summary under 280 characters.");
        return null;
    }
}
