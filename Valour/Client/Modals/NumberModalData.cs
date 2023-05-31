namespace Valour.Client.Modals;

public class NumberModalData
{
    /// <summary>
    /// Run if the user hits "confirm"
    /// </summary>
    public Func<int, Task> ConfirmEvent;

    /// <summary>
    /// Run if the user hits "cancel"
    /// </summary>
    public Func<Task> CancelEvent;


    // Cosmetics
    public string TitleText { get; set; }
    public string DescText { get; set; }
    public string ConfirmText { get; set; }
    public string CancelText { get; set; }

    public NumberModalData()
    {
    }
    
    public NumberModalData(string title, string desc, string confirm, string cancel, Func<int, Task> OnConfirm, Func<Task> OnCancel)
    {
        TitleText = title;
        DescText = desc;
        ConfirmText = confirm;
        CancelText = cancel;

        ConfirmEvent = OnConfirm;
        CancelEvent = OnCancel;
    }
}