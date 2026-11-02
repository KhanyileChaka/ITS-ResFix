using Microsoft.AspNetCore.Mvc;

namespace ITSResFix.Staff.Helpers;

public static class ControllerExtensions
{
    /// <summary>Puts an error banner on the next page and returns the redirect as it was.</summary>
    public static IActionResult WithError(this IActionResult result, Controller controller, string message)
    {
        controller.TempData["Error"] = message;
        return result;
    }
}
