using Microsoft.EntityFrameworkCore;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.Services;

public static class UserErrorHandler
{
    public static string GetMessage(Exception exception, string operation)
    {
        if (exception is PosRuleException ruleFailure)
        {
            Serilog.Log.Warning(ruleFailure, "{Operation} was rejected by a business rule", operation);
            return ruleFailure.Message;
        }

        Serilog.Log.Error(exception, "{Operation} failed", operation);
        return exception switch
        {
            UnauthorizedAccessException => "Your account does not have permission to perform this operation.",
            ArgumentException => "Check the values entered and try again.",
            DbUpdateConcurrencyException => "The product or stock changed in another window. Refresh the data and try again.",
            DbUpdateException => "The record could not be saved. It may conflict with an existing record or related data.",
            _ => $"{operation} failed. Check the application log under {SmartPosPaths.LogsDirectory} for details."
        };
    }
}
