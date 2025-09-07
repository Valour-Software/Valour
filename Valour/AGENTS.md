# Valour Project - AI Agent Instructions

This document provides guidelines and patterns for AI assistants working on the Valour project.

# Critical

All changes must be complete, clean, and extendable. You should never add TODO or incomplete solutions.

Avoid comments unless the code complexity warrants them. Always avoid comments in CSS.

## 🎨 UI Components & Patterns

### BasicModalLayout Usage

The project uses a standardized `BasicModalLayout` component for all modals. **Always use this instead of custom modal structures.**

```razor
<BasicModalLayout Title="Modal Title" Icon="icon-name" MaxWidth="500px">
    <MainArea>
        <!-- Main content goes here -->
        <div class="form-group">
            <label>Field Label</label>
            <input class="form-control" @bind-value="@_value" />
        </div>
        
        <!-- Use ResultLabel for operation results -->
        <ResultLabel Result="@_result" />
    </MainArea>
    <ButtonArea>
        <div class="basic-modal-buttons">
            <button @onclick="@OnCancel" class="v-btn">Cancel</button>
            <button @onclick="@OnSubmit" class="v-btn primary">Submit</button>
        </div>
    </ButtonArea>
</BasicModalLayout>
```

**Key Points:**
- Use `MainArea` for content, `ButtonArea` for actions
- Always include `ResultLabel` for operation feedback
- Use `basic-modal-buttons` class for button layout
- Include cancel functionality

### Toast Notifications

Use toasts for actions that can fail or need user feedback. **Don't use modals for simple success/error messages.**

The project uses `ToastContainer.Instance` for all toast operations. There are several types of toasts:

#### Simple Toasts
```csharp
// Simple notification toast
ToastContainer.Instance.AddToast(new ToastData("Success!", "Operation completed successfully"));
```

#### Progress Toasts (Recommended for async operations)
```csharp
// For operations that return TaskResult
var result = await ToastContainer.Instance.WaitToastWithTaskResult(new ProgressToastData<TaskResult>()
{
    ProgressTask = Service.DoSomethingAsync(),
    Title = "Processing",
    Message = "Please wait...",
    SuccessMessage = "Operation completed!",
    FailureMessage = "Operation failed!"
});

// For operations that return any result type
var result = await ToastContainer.Instance.WaitToastWithResult(new ProgressToastData<MyResultType>()
{
    ProgressTask = Service.DoSomethingAsync(),
    Title = "Processing",
    Message = "Please wait..."
});
```

#### Progress Toast States
Progress toasts automatically show:
- **Running**: Spinning animation while task is executing
- **Success**: Green checkmark when task succeeds
- **Failure**: Red X when task fails

**When to use toasts:**
- ✅ Async operations that can fail (use progress toasts)
- ✅ Operation success/failure feedback
- ✅ Non-blocking notifications
- ✅ Quick status updates
- ❌ Complex forms or multi-step processes (use modals)

## 🎨 Styling Guidelines

### Global Styles

**All global styles go in `Client/wwwroot/css/globals.css`** - never create new global CSS files.

**Available CSS Variables:**

CSS variables can be checked at the top of the globals file. The v- colors are 'vibrant'. The p- colors are 'pastel'.

### Component-Specific Styles

Use `.razor.css` files for component-specific styles:

```css
/* ComponentName.razor.css */
.my-component {
    /* Component styles */
}

.my-component .child-element {
    /* Child element styles */
}
```

### Common CSS Classes

**Layout:**
- `.form-group` - Form field container
- `.input-group` - Input with button
- `.editor-section` - Content section with border
- `.basic-modal-buttons` - Modal button layout

**Buttons:**
- `.v-btn` - Base button
- `.v-btn.primary` - Primary action
- `.v-btn.danger` - Destructive action
- `.v-btn.secondary` - Secondary action

**Text:**
- `.subtitle` - Section subtitle
- `.helper-text` - Field help text
- `.status-badge` - Status indicators

## 🏗️ Project Structure

### Key Directories

```
Client/
├── Components/           # Blazor components
├── wwwroot/
│   └── css/
│       └── globals.css  # Global styles ONLY
└── _Imports.razor       # Global using statements

Server/
├── Api/                 # API endpoints
├── Services/            # Business logic
└── Models/              # Server-side models

Sdk/
├── Services/            # Client services
├── Models/              # Shared models
└── Requests/            # API request models
```

### Component Patterns

**Modal Components:**
```csharp
@inherits Modal<ComponentName.ModalParams>

public class ModalParams
{
    // Parameters for the modal
}

// Always include:
private void OnCancel() => Close();
```

**Service Components:**
```csharp
@inject ValourClient Client
@inject SpecificService Service

// Use TaskResult for operations
private ITaskResult _result;
```

## 🔧 Common Patterns

### Form Validation

```csharp
private async Task OnSubmit()
{
    // Validate input
    if (string.IsNullOrWhiteSpace(_value))
    {
        _result = new TaskResult(false, "Field is required");
        StateHasChanged();
        return;
    }

    // Perform operation
    var response = await Service.DoSomething(_value);
    
    if (response.Success)
    {
        _result = new TaskResult(true, "Success!");
        Close();
    }
    else
    {
        _result = new TaskResult(false, response.Message);
        StateHasChanged();
    }
}
```

### API Calls

```csharp
// Use the service pattern
var result = await OauthService.CreateAppAsync(request);

// Handle TaskResult
if (result.Success)
{
    // Success handling
}
else
{
    // Error handling
}
```

### State Management

```csharp
// Loading states
private bool _isLoading;
private List<Item> _items;

protected override async Task OnInitializedAsync()
{
    await LoadData();
}

private async Task LoadData()
{
    _isLoading = true;
    StateHasChanged();
    
    try
    {
        _items = await Service.GetItemsAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to load data: {ex.Message}");
        _items = new List<Item>();
    }
    finally
    {
        _isLoading = false;
        StateHasChanged();
    }
}
```

## 🚫 Common Mistakes to Avoid

1. **Don't create custom modal structures** - Always use `BasicModalLayout`
2. **Don't put global styles in component CSS** - Use `globals.css`
3. **Don't use modals for simple notifications** - Use toasts instead
4. **Don't forget error handling** - Always handle API failures
5. **Don't skip loading states** - Show loading indicators for async operations
6. **Don't use inline styles** - Use CSS classes and variables

## 📝 Code Style

### Naming Conventions

- **Components**: PascalCase (e.g., `EditUserComponent`)
- **Files**: Match component name (e.g., `EditUserComponent.razor`)
- **CSS Classes**: kebab-case (e.g., `user-profile`)
- **Private Fields**: Underscore prefix (e.g., `_isLoading`)
- **Methods**: PascalCase (e.g., `OnSubmit`)

### File Organization

```
ComponentName.razor          # Component markup
ComponentName.razor.css      # Component styles
ComponentName.razor.cs       # Component logic (if complex)
```

## 🔍 Debugging Tips

1. **Check browser console** for JavaScript errors
2. **Client services have debug methods** for debugging 
3. **Check network tab** for API call failures
5. **Test on different screen sizes** - responsive design is important

## 📚 Useful Resources

- **CSS Variables**: See `globals.css` for all available variables
- **Component Library**: Check existing components for patterns
- **API Documentation**: See `Sdk/Services/` for available services
- **Modal Examples**: Look at existing modals for patterns

## 🔍 HybridEvent Usage

The `HybridEvent<T>` and `HybridEvent` classes provide efficient event handling that supports both synchronous and asynchronous handlers with object pooling for performance.

### Basic Usage

```csharp
// Create a HybridEvent with data
public HybridEvent<string> OnSearchChanged { get; set; } = new();

// Subscribe to events
OnSearchChanged += async (searchTerm) => {
    await PerformSearch(searchTerm);
};

// Invoke the event
OnSearchChanged?.Invoke("search term");
```

### Parameterless Events

```csharp
// Create a parameterless HybridEvent
public HybridEvent OnSearchCleared { get; set; } = new();

// Subscribe to events
OnSearchCleared += async () => {
    await ClearResults();
};

// Invoke the event
OnSearchCleared?.Invoke();
```

### Component Integration

For component parameters, use `EventCallback` instead of `HybridEvent`:

```csharp
@code {
    [Parameter] public EventCallback<string> OnSearchChanged { get; set; }
    [Parameter] public EventCallback OnSearchCleared { get; set; }
    
    private async Task HandleInput(ChangeEventArgs e)
    {
        var value = e.Value?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            await OnSearchCleared.InvokeAsync();
        }
        else
        {
            await OnSearchChanged.InvokeAsync(value);
        }
    }
}
```

For internal component events, use `HybridEvent`:

```csharp
@code {
    public HybridEvent<string> OnSearchChanged { get; } = new();
    public HybridEvent OnSearchCleared { get; } = new();
    
    private async Task HandleInput(ChangeEventArgs e)
    {
        var value = e.Value?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            OnSearchCleared?.Invoke();
        }
        else
        {
            OnSearchChanged?.Invoke(value);
        }
    }
    
    public ValueTask DisposeAsync()
    {
        OnSearchChanged?.Dispose();
        OnSearchCleared?.Dispose();
        return ValueTask.CompletedTask;
    }
}
```

### Key Benefits

- **Performance**: Uses object pooling to minimize allocations
- **Thread-Safe**: Handles concurrent access with proper locking
- **Flexible**: Supports both sync and async handlers
- **Memory Efficient**: Automatically manages handler lists
- **Clean API**: Simple += and -= operators for subscription

### Best Practices

- Always dispose HybridEvents in component disposal
- Use parameterized events when you need to pass data
- Use parameterless events for simple notifications
- Prefer async handlers for I/O operations
- Use sync handlers for simple state updates

## 🔍 ModelQueryEngine API Reference

The `ModelQueryEngine<T>` provides a powerful API for querying data with filtering, sorting, and paging:

### Filtering
```csharp
// Set a filter (automatically applies and resets paging)
queryEngine.SetFilter("search", "search term", apply: true);

// Set multiple filters
queryEngine.SetFilter("category", "themes");
queryEngine.SetFilter("status", "active");

// Clear a specific filter
queryEngine.SetFilter("search", null, apply: true);
```

### Sorting
```csharp
// Set sort field and direction
queryEngine.SetSort("name", descending: false, apply: true);

// Clear sorting
queryEngine.ClearSort();
```

### Paging
```csharp
// Get total count
int total = await queryEngine.GetTotalCountAsync();

// Get specific page
var page = await queryEngine.GetPageAsync(pageIndex: 0, pageSize: 20);

// Navigate pages
var nextPage = await queryEngine.NextPageAsync();
var prevPage = await queryEngine.PreviousPageAsync();

// Refresh current page
var refreshed = await queryEngine.RefreshCurrentPageAsync();
```

### Random Access
```csharp
// Get item at specific index
var item = await queryEngine.GetAtIndexAsync(index: 5);

// Get range of items
var response = await queryEngine.GetItemsAsync(skip: 10, take: 5);
```

### State Management
```csharp
// Check paging state
bool isFirst = queryEngine.IsFirstPage;
bool isLast = queryEngine.IsLastPage;
int currentPage = queryEngine.CurrentPageIndex;
int pageSize = queryEngine.PageSize;
int totalCount = queryEngine.TotalCount;

// Reset paging (keeps filters/sort)
queryEngine.ResetPaging();

// Apply all options (resets paging)
queryEngine.ApplyOptions();
```

## 🎯 Quick Reference

**Modal Template:**
```razor
<BasicModalLayout Title="Title" Icon="icon" MaxWidth="500px">
    <MainArea>
        <!-- Content -->
        <ResultLabel Result="@_result" />
    </MainArea>
    <ButtonArea>
        <div class="basic-modal-buttons">
            <button @onclick="@OnCancel" class="v-btn">Cancel</button>
            <button @onclick="@OnSubmit" class="v-btn primary">Submit</button>
        </div>
    </ButtonArea>
</BasicModalLayout>
```

**Toast Usage:**
```csharp
// Simple toast
ToastContainer.Instance.AddToast(new ToastData("Title", "Message"));

// Progress toast (recommended for async operations)
var result = await ToastContainer.Instance.WaitToastWithTaskResult(new ProgressToastData<TaskResult>()
{
    ProgressTask = Service.DoSomethingAsync(),
    Title = "Processing",
    Message = "Please wait...",
    SuccessMessage = "Success!",
    FailureMessage = "Failed!"
});
```

**Form Group:**
```razor
<div class="form-group">
    <label>Label</label>
    <input class="form-control" @bind-value="@_value" />
    <span class="helper-text">Help text</span>
</div>
```

---

**Remember**: When in doubt, look at existing components for patterns and examples!
