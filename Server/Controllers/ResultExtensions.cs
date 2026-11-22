using Microsoft.AspNetCore.Mvc;
using Server.Models;

namespace Server.Controllers;

public static class ResultExtensions
{
    public static IActionResult ToActionResult<T>(this ControllerBase controller, Result<T> result, int successStatus = 200)
    {
        if (result.Success)
            return controller.StatusCode(successStatus, result.Value);

        var code = result.ErrorCode ?? "";
        var status = code switch
        {
            "VALIDATION_ERROR" or "EXCEEDS_CAPACITY" => 400,
            "FORBIDDEN" => 403,
            "DUPLICATE_CODE" or "INVALID_STATE" or "NOTHING_TO_RESTOCK" => 409,
            _ when code.EndsWith("_NOT_FOUND") => 404,
            _ => 400
        };
        return controller.StatusCode(status, new { error = new { code, message = result.ErrorMessage } });
    }
}