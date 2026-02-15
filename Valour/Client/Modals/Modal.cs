using Microsoft.AspNetCore.Components;

namespace Valour.Client.Modals;

public class Modal<T> : ComponentBase
{
    [CascadingParameter]
    public ModalRoot ModalRoot { get; set; }
    
    [Parameter]
    public string ModalId { get; set; }
    
    [Parameter]
    public T Data { get; set; }

    public virtual void Close()
    {
        ModalRoot?.CloseModal(ModalId);
    }
}
