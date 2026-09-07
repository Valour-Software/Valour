# Valour development guidance

Complete changes fully and keep them understandable and extendable. Do not leave
TODOs or incomplete implementations. Add code comments only when complexity needs
an explanation, and do not add comments to CSS.

## UI components

Use `BasicModalLayout` for modals. Put content in `MainArea` and actions in
`ButtonArea`, using `basic-modal-buttons` for the button layout. Include cancel
behavior and `ResultLabel` for operation feedback. Modal components inherit
`Modal<TParams>` and close through `Close()`.

```razor
<BasicModalLayout Title="Title" Icon="icon" MaxWidth="500px">
    <MainArea>
        <ResultLabel Result="@_result" />
    </MainArea>
    <ButtonArea>
        <div class="basic-modal-buttons">
            <button @onclick="@OnCancel" class="v-btn">Cancel</button>
            <button @onclick="@OnSubmit" class="v-btn primary">Save</button>
        </div>
    </ButtonArea>
</BasicModalLayout>
```

Use `ToastContainer.Instance.AddToast(new ToastData(title, message))` for short
notifications. For asynchronous operations, use `WaitToastWithTaskResult` with
`ProgressToastData<TaskResult>` or `WaitToastWithResult` with the appropriate
result type. Progress toasts display running, success, and failure states. Forms
and multi-step interactions belong in modals; simple operation feedback uses toasts.

Validate input before calling a service, show a loading state while waiting, and
handle unsuccessful `TaskResult` responses. Use SDK services for API access.
Components inheriting `ControlledRenderComponentBase` must request visible updates
with `ReRender()`. Use the normal Blazor render path for other component bases.

## Styling

Put global styles in `Client/wwwroot/css/globals.css` and component styles in the
component's `.razor.css` file. Use CSS classes and the existing variables rather
than inline styles. The variables at the top of `globals.css` define the palette;
`v-` colors are vibrant and `p-` colors are pastel.

Use existing form and button classes such as `form-group`, `input-group`,
`basic-modal-buttons`, and `v-btn` with its `primary`, `secondary`, or `danger`
variant. Check the stylesheet and nearby components before relying on a class.
Verify layouts at the pane sizes the component actually receives, including touch
input where supported.

## Naming and files

Component names and methods use PascalCase. Private fields use an underscore
prefix, and CSS classes use kebab-case. Keep markup in `ComponentName.razor`,
styles in `ComponentName.razor.css`, and substantial separated C# logic in
`ComponentName.razor.cs`.

Shared UI is in `Client/`; the browser host is `Client.Blazor/`. SDK services and
models are in `Sdk/`, shared contracts in `Shared/`, and server routes and services
in `Server/Api/` and `Server/Services/`.

## Events and disposal

Use `EventCallback` or `EventCallback<T>` for parent/child component parameters.
Internal events can use `HybridEvent` or `HybridEvent<T>`. Its invocation runs
synchronous handlers and starts asynchronous handlers without awaiting completion.
Do not use it as a completion signal for dependent work.

Keep named delegates for subscriptions and remove them during disposal. Dispose
events created by the component, not shared model events it only observes. Release
JavaScript instances, listeners, animation loops, and interop references according
to their owning component's lifecycle. Initialization can finish after disposal;
late results must be released without reactivating the component.

Read [Reactive models](../Docs/ReactiveModelSystem.md) before changing cache or event
behavior. Preserve origin scope for models from community nodes.

## Querying models

`ModelQueryEngine<T>` supports named filters, sorting, paging, and indexed access.
`SetFilter` and `SetSort` apply options by default, resetting paging. For several
changes, pass `apply: false` to each and call `ApplyOptions()` once. Set a filter to
null to remove it, and use `ClearSort()` to clear sorting.

`GetPageAsync(pageIndex, pageSize)` loads a page. `NextPageAsync`,
`PreviousPageAsync`, and `RefreshCurrentPageAsync` use the paging state.
`GetAtIndexAsync` returns an item, and `GetItemsAsync(skip, take)` returns a query
response. `GetTotalCountAsync` fetches the count if needed; the `TotalCount`
property is -1 while it is unknown. `ResetPaging()` retains filters and sort.

## Verification and documentation

Inspect browser console and network errors when debugging UI or API failures.
Run relevant tests against isolated services as described in the root guidance
and test READMEs. Check responsive layouts and interaction with actual touch events
when the change affects mobile controls.

Documentation describes current code and working procedures. Explain concepts in
ordinary English, without em dashes, implementation history, or promotional
language. Link to source and other guides instead of copying long implementations.
