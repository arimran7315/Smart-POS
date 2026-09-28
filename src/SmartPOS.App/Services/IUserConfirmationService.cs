namespace SmartPOS.App.Services;

public interface IUserConfirmationService
{
    Task<bool> ConfirmAsync(string title, string message, ConfirmationKind kind = ConfirmationKind.Question);
}

public enum ConfirmationKind
{
    Question,
    Warning
}
