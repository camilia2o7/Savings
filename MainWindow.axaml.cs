using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia.Threading;
namespace Savings;
/// <summary>
/// Ok lets make this clear, i hate adding comments, so don't come for me for the slack of comments, and also i'd like to note
/// most of these are handled differently then what i am normally used to, such as colors/titles, it reminds me of windows forms
/// a little bit lmfao, but we thug it out, anywho, this p much discloses it
/// also wanted to note that i LOVE https://learn.microsoft.com/en-us/windows/apps/design/style/segoe-ui-symbol-font
/// UPDATE: Now with data saving because losing your savings goals sucks ass
/// UPDATE 2: Now with tags and overview because organization is cool
/// </summary>
public partial class MainWindow : Window
{
    #region Private Fields
    // Collections to store our savings data, might change it up
    private ObservableCollection<SavingsGoal> _savingsGoals = new();
    private ObservableCollection<GoalDisplayItem> _goalDisplayItems = new();

    // Track which goal is currently selected by the user ok
    private SavingsGoal? _selectedGoal = null;
    private SavingsGoal? _editingTagsGoal = null;

    // Variables for custom resize functionality
    private bool _isResizing = false;
    private Avalonia.Point _lastPointerPosition;

    // Data persistence stuff
    private readonly string _dataFolderPath;
    private readonly string _goalsFilePath;

    // Tag filtering and management
    private HashSet<string> _allExistingTags = new();
    private List<string> _filteredTags = new();
    private bool _isTagDropdownOpen = false;

    private bool _showingArchivedGoals = false;
    public bool IsArchived { get; set; } = false;
    #endregion

    #region Tag Filtering and Autocomplete
    private void UpdateAllExistingTags()
    {
        _allExistingTags.Clear();
        foreach (var goal in _savingsGoals)
        {
            foreach (var tag in goal.Tags)
            {
                _allExistingTags.Add(tag);
            }
        }
    }
    private void FilterTags(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            _filteredTags.Clear();
            return;
        }

        _filteredTags = _allExistingTags
            .Where(tag => tag.StartsWith(input, StringComparison.OrdinalIgnoreCase))
            .OrderBy(tag => tag)
            .ToList();
    }
    private void ShowTagSuggestions(TextBox textBox, StackPanel suggestionsPanel)
    {
        var parentBorder = suggestionsPanel.Parent as Border;
        if (_filteredTags.Count == 0)
        {
            if (parentBorder != null) parentBorder.IsVisible = false;
            return;
        }
        suggestionsPanel.Children.Clear();
        if (parentBorder != null) parentBorder.IsVisible = true;
        foreach (var tag in _filteredTags.Take(5))
        {
            var suggestionButton = new Button
            {
                Content = tag,
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.Parse("#374151")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 8),
                CornerRadius = new CornerRadius(0),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            suggestionButton.Click += (s, e) => {
                textBox.Text = tag;
                if (parentBorder != null) parentBorder.IsVisible = false;
            };
            suggestionButton.PointerEntered += (s, e) => {
                suggestionButton.Background = new SolidColorBrush(Color.Parse("#374151"));
            };
            suggestionButton.PointerExited += (s, e) => {
                suggestionButton.Background = Brushes.Transparent;
            };
            suggestionsPanel.Children.Add(suggestionButton);
        }
    }
    #endregion

    #region Constructor & Initialization
    /// Initialize the main window and set up all event handlers + load saved data
    public MainWindow()
    {
        InitializeComponent();
        _dataFolderPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        _goalsFilePath = System.IO.Path.Combine(_dataFolderPath, "goals.cfg");
        Directory.CreateDirectory(_dataFolderPath);
        AttachEventHandlers();
        LoadGoalsFromFile();
        UpdateUI();
    }

    private void AttachEventHandlers()
    {
        AttachTitleBarHandlers();
        AttachApplicationEventHandlers();
        this.Closing += OnWindowClosing;
    }
    private void AttachTitleBarHandlers()
    {
        var titleBarGrid = this.FindControl<Grid>("TitleBarGrid");
        if (titleBarGrid != null)
        {
            titleBarGrid.PointerPressed += OnTitleBarPointerPressed;
            titleBarGrid.PointerMoved += OnTitleBarPointerMoved;
            titleBarGrid.PointerReleased += OnTitleBarPointerReleased;
        }
        var minimizeButton = this.FindControl<Button>("MinimizeButton");
        var closeButton = this.FindControl<Button>("CloseButton");
        if (minimizeButton != null)
            minimizeButton.Click += OnMinimizeClick;
        if (closeButton != null)
            closeButton.Click += OnCloseClick;
        var resizeGrip = this.FindControl<Rectangle>("ResizeGrip");
        if (resizeGrip != null)
        {
            resizeGrip.PointerPressed += OnResizeGripPointerPressed;
            resizeGrip.PointerMoved += OnResizeGripPointerMoved;
            resizeGrip.PointerReleased += OnResizeGripPointerReleased;
        }
    }
    private void AttachApplicationEventHandlers()
    {
        var addGoalButton = this.FindControl<Button>("AddSavingsGoalButton");
        var addMoneyButton = this.FindControl<Button>("AddMoneyButton");
        var confirmAddButton = this.FindControl<Button>("ConfirmAddMoneyButton");
        var cancelAddButton = this.FindControl<Button>("CancelAddMoneyButton");
        var goalSelector = this.FindControl<ComboBox>("GoalSelector");
        var addTagButton = this.FindControl<Button>("AddTagToGoalButton");
        var cancelTagButton = this.FindControl<Button>("CancelTagEditButton");

        // Tag input boxes with autocomplete
        var tagTextBox = this.FindControl<TextBox>("TagTextBox");
        var newTagTextBox = this.FindControl<TextBox>("NewTagTextBox");
        var tagSuggestions = this.FindControl<StackPanel>("TagSuggestions");
        var newTagSuggestions = this.FindControl<StackPanel>("NewTagSuggestions");
        var archiveButton = this.FindControl<Button>("ArchiveButton");
        var backToGoalsButton = this.FindControl<Button>("BackToGoalsButton");
        if (archiveButton != null)
            archiveButton.Click += OnArchiveButtonClick;
        if (backToGoalsButton != null)
            backToGoalsButton.Click += OnBackToGoalsClick;
        if (addGoalButton != null)
            addGoalButton.Click += OnAddSavingsGoalClick;
        if (addMoneyButton != null)
            addMoneyButton.Click += OnAddMoneyClick;
        if (confirmAddButton != null)
            confirmAddButton.Click += OnConfirmAddMoneyClick;
        if (cancelAddButton != null)
            cancelAddButton.Click += OnCancelAddMoneyClick;
        if (addTagButton != null)
            addTagButton.Click += OnAddTagToGoalClick;
        if (cancelTagButton != null)
            cancelTagButton.Click += OnCancelTagEditClick;
        if (goalSelector != null)
        {
            goalSelector.ItemsSource = _goalDisplayItems;
            goalSelector.SelectionChanged += OnGoalSelectorChanged;
        }

        // Tag autocomplete event handlers
        if (tagTextBox != null && tagSuggestions != null)
        {
            tagTextBox.TextChanged += (s, e) => {
                UpdateAllExistingTags();
                FilterTags(tagTextBox.Text ?? "");
                ShowTagSuggestions(tagTextBox, tagSuggestions);
            };

            tagTextBox.LostFocus += (s, e) => {
                Task.Delay(200).ContinueWith(_ => {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        var border = this.FindControl<Border>("TagSuggestionsBorder");
                        if (border != null) border.IsVisible = false;
                    });
                });
            };
        }

        if (newTagTextBox != null && newTagSuggestions != null)
        {
            newTagTextBox.TextChanged += (s, e) => {
                UpdateAllExistingTags();
                FilterTags(newTagTextBox.Text ?? "");
                ShowTagSuggestions(newTagTextBox, newTagSuggestions);
            };
            newTagTextBox.LostFocus += (s, e) => {
                Task.Delay(200).ContinueWith(_ => {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        if (NewTagSuggestionsBorder != null) NewTagSuggestionsBorder.IsVisible = false;
                    });
                });
            };
        }
    }
    #endregion

    #region Data Persistence
    /// Save goals to file when window is closing
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        SaveGoalsToFile();
    }

    /// Save all goals to the config file
    private void SaveGoalsToFile()
    {
        try
        {
            var goalsData = _savingsGoals.Select(g => new SavingsGoalData
            {
                Id = g.Id,
                Name = g.Name,
                TargetAmount = g.TargetAmount,
                CurrentAmount = g.CurrentAmount,
                CreatedDate = g.CreatedDate,
                Tags = g.Tags.ToList(),
                IsArchived = g.IsArchived
            }).ToList();

            var json = JsonSerializer.Serialize(goalsData, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_goalsFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save goals: {ex.Message}");
        }
    }
    /// Load goals from the config file
    private void LoadGoalsFromFile()
    {
        try
        {
            if (!File.Exists(_goalsFilePath))
                return;

            var json = File.ReadAllText(_goalsFilePath);
            var goalsData = JsonSerializer.Deserialize<List<SavingsGoalData>>(json);

            if (goalsData != null)
            {
                _savingsGoals.Clear();
                foreach (var data in goalsData)
                {
                    _savingsGoals.Add(new SavingsGoal
                    {
                        Id = data.Id,
                        Name = data.Name,
                        TargetAmount = data.TargetAmount,
                        CurrentAmount = data.CurrentAmount,
                        CreatedDate = data.CreatedDate,
                        Tags = new ObservableCollection<string>(data.Tags ?? new List<string>()),
                        IsArchived = data.IsArchived
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load goals: {ex.Message}");
            _savingsGoals.Clear();
        }
    }
    #endregion

    #region Custom Title Bar Event Handlers
    /// Handle mouse clicks on the title bar to start window dragging
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
    private void OnTitleBarPointerMoved(object? sender, PointerEventArgs e)
    {
        // The actual dragging is handled by BeginMoveDrag automatically so don't add/change anything here
    }
    private void OnTitleBarPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // The drag end is handled by BeginMoveDrag automatically so don't add/change anthing here
    }
    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }
    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
    #endregion

    #region Custom Resize Functionality
    /// Start resizing when user clicks the bottom-right resize grip
    /// Update - I hate Ava because the colors bro
    private void OnResizeGripPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isResizing = true;
            _lastPointerPosition = e.GetCurrentPoint(this).Position;
            e.Pointer.Capture(sender as Control);
        }
    }
    private void OnResizeGripPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isResizing)
        {
            var currentPosition = e.GetCurrentPoint(this).Position;
            var deltaX = currentPosition.X - _lastPointerPosition.X;
            var deltaY = currentPosition.Y - _lastPointerPosition.Y;
            var newWidth = Math.Max(MinWidth, Width + deltaX);
            var newHeight = Math.Max(MinHeight, Height + deltaY);
            Width = newWidth;
            Height = newHeight;
            _lastPointerPosition = currentPosition;
        }
    }
    private void OnResizeGripPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isResizing)
        {
            _isResizing = false;
            e.Pointer.Capture(null);
        }
    }
    #endregion

    #region Savings Goal Management
    /// Handle creating a new savings goal when Master E fills out the form//i should make this error randomly on purpose lmfao
    private async void OnAddSavingsGoalClick(object? sender, RoutedEventArgs e)
    {
        var nameTextBox = this.FindControl<TextBox>("ItemNameTextBox");
        var targetTextBox = this.FindControl<TextBox>("TargetAmountTextBox");
        var initialTextBox = this.FindControl<TextBox>("InitialAmountTextBox");
        var tagTextBox = this.FindControl<TextBox>("TagTextBox");

        if (nameTextBox == null || targetTextBox == null || initialTextBox == null)
            return;
        HideError();
        var name = nameTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ShowError("Please tell us what you're saving for.");
            return;
        }
        if (!decimal.TryParse(targetTextBox.Text?.Replace("$", "").Replace(",", ""), out var targetAmount) || targetAmount <= 0)
        {
            ShowError("Please enter how much it costs (must be greater than 0).");
            return;
        }
        var initialText = initialTextBox.Text?.Replace("$", "").Replace(",", "");
        if (string.IsNullOrEmpty(initialText)) initialText = "0";

        if (!decimal.TryParse(initialText, out var initialAmount) || initialAmount < 0)
        {
            ShowError("Starting deposit must be 0 or greater.");
            return;
        }
        if (initialAmount > targetAmount)
        {
            ShowError("Starting deposit can't be more than the total cost.");
            return;
        }
        if (_savingsGoals.Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowError("You're already saving for this! Choose a different name.");
            return;
        }

        // Parse tags from input
        var tags = new ObservableCollection<string>();
        if (!string.IsNullOrWhiteSpace(tagTextBox?.Text))
        {
            var tagInputs = tagTextBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var tag in tagInputs)
            {
                var cleanTag = tag.Trim();
                if (!string.IsNullOrEmpty(cleanTag) && !tags.Contains(cleanTag, StringComparer.OrdinalIgnoreCase))
                {
                    tags.Add(cleanTag);
                }
            }
        }

        var goal = new SavingsGoal
        {
            Id = Guid.NewGuid(),
            Name = name,
            TargetAmount = targetAmount,
            CurrentAmount = initialAmount,
            CreatedDate = DateTime.Now,
            Tags = tags
        };
        _savingsGoals.Add(goal);
        nameTextBox.Text = "";
        targetTextBox.Text = "";
        initialTextBox.Text = "";
        if (tagTextBox != null) tagTextBox.Text = "";
        SaveGoalsToFile();
        UpdateUI();
        ShowError($"Started saving for '{name}'! Keep adding money to reach your goal.", "#10b981");
        await System.Threading.Tasks.Task.Delay(3000);
        HideError();
    }
    private void OnAddMoneyClick(object? sender, RoutedEventArgs e)
    {
        if (_savingsGoals.Count == 0)
        {
            ShowError("Start saving for something first!");
            return;
        }
        var addMoneySection = this.FindControl<StackPanel>("AddMoneySection");
        var goalSelector = this.FindControl<ComboBox>("GoalSelector");
        if (addMoneySection == null || goalSelector == null) return;
        addMoneySection.IsVisible = true;
        var incompleteGoal = _savingsGoals.FirstOrDefault(g => !g.IsCompleted);
        if (incompleteGoal != null && goalSelector != null)
        {
            var displayItem = _goalDisplayItems.FirstOrDefault(item => item.Goal.Id == incompleteGoal.Id);
            if (displayItem != null)
            {
                goalSelector.SelectedItem = displayItem;
            }
        }
    }
    private void OnCancelAddMoneyClick(object? sender, RoutedEventArgs e)
    {
        var addMoneySection = this.FindControl<StackPanel>("AddMoneySection");
        var amountTextBox = this.FindControl<TextBox>("AddMoneyAmountTextBox");
        if (addMoneySection != null) addMoneySection.IsVisible = false;
        if (amountTextBox != null) amountTextBox.Text = "";
        HideError();
    }
    private async void OnConfirmAddMoneyClick(object? sender, RoutedEventArgs e)
    {
        var amountTextBox = this.FindControl<TextBox>("AddMoneyAmountTextBox");
        var goalSelector = this.FindControl<ComboBox>("GoalSelector");
        if (amountTextBox == null || goalSelector == null) return;
        var selectedItem = goalSelector.SelectedItem as GoalDisplayItem;
        var selectedGoal = selectedItem?.Goal;
        if (selectedGoal == null)
        {
            ShowError("Please choose what you want to add money to.");
            return;
        }
        var amountText = amountTextBox.Text?.Replace("$", "").Replace(",", "");
        if (!decimal.TryParse(amountText, out var amount) || amount <= 0)
        {
            ShowError("Please enter how much money you want to add (must be greater than 0).");
            return;
        }
        if (selectedGoal.IsCompleted)
        {
            ShowError("You've already saved enough for this! You're done!");
            return;
        }
        var oldAmount = selectedGoal.CurrentAmount;
        selectedGoal.CurrentAmount += amount;
        var wasCompleted = selectedGoal.IsCompleted;
        if (selectedGoal.CurrentAmount > selectedGoal.TargetAmount)
        {
            selectedGoal.CurrentAmount = selectedGoal.TargetAmount;
            amount = selectedGoal.TargetAmount - oldAmount;
        }
        amountTextBox.Text = "";
        var addMoneySection = this.FindControl<StackPanel>("AddMoneySection");
        if (addMoneySection != null) addMoneySection.IsVisible = false;
        // Save immediately after adding savings
        SaveGoalsToFile();
        UpdateUI();
        var message = wasCompleted ?
            $"Congratulations! You've saved enough for your {selectedGoal.Name}! (Added ${amount:F2})" :
            $"Added ${amount:F2} to your {selectedGoal.Name} savings!";
        ShowError(message, "#10b981");
        await System.Threading.Tasks.Task.Delay(3000);
        HideError();
    }
    private void OnGoalSelectorChanged(object? sender, SelectionChangedEventArgs e)
    {
        var goalSelector = sender as ComboBox;
        var selectedItem = goalSelector?.SelectedItem as GoalDisplayItem;
        _selectedGoal = selectedItem?.Goal;
        UpdateGoalCards();
    }
    private void OnGoalCardClick(SavingsGoal goal)
    {
        _selectedGoal = goal;
        var goalSelector = this.FindControl<ComboBox>("GoalSelector");
        if (goalSelector != null)
        {
            var displayItem = _goalDisplayItems.FirstOrDefault(item => item.Goal.Id == goal.Id);
            if (displayItem != null)
            {
                goalSelector.SelectedItem = displayItem;
            }
        }
        UpdateGoalCards();
    }
    private void OnDeleteGoalClick(SavingsGoal goal)
    {
        _savingsGoals.Remove(goal);
        if (_selectedGoal?.Id == goal.Id)
            _selectedGoal = null;
        SaveGoalsToFile();
        UpdateUI();
    }
    #endregion

    #region Tag Management
    private void OnAddTagButtonClick(SavingsGoal goal)
    {
        _editingTagsGoal = goal;
        var tagSection = this.FindControl<StackPanel>("TagEditorSection");
        var newTagTextBox = this.FindControl<TextBox>("NewTagTextBox");

        if (tagSection != null) tagSection.IsVisible = true;
        if (newTagTextBox != null) newTagTextBox.Text = "";
    }
    private async void OnAddTagToGoalClick(object? sender, RoutedEventArgs e)
    {
        if (_editingTagsGoal == null) return;
        var newTagTextBox = this.FindControl<TextBox>("NewTagTextBox");
        if (newTagTextBox == null) return;
        var tagName = newTagTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(tagName))
        {
            ShowError("Please enter a tag name.");
            return;
        }
        if (_editingTagsGoal.Tags.Contains(tagName, StringComparer.OrdinalIgnoreCase))
        {
            ShowError("This goal already has that tag.");
            return;
        }
        _editingTagsGoal.Tags.Add(tagName);
        SaveGoalsToFile();
        UpdateUI();
        var tagSection = this.FindControl<StackPanel>("TagEditorSection");
        if (tagSection != null) tagSection.IsVisible = false;
        ShowError($"Added '{tagName}' tag to {_editingTagsGoal.Name}!", "#10b981");
        await System.Threading.Tasks.Task.Delay(2000);
        HideError();

        _editingTagsGoal = null;
    }
    private void OnCancelTagEditClick(object? sender, RoutedEventArgs e)
    {
        var tagSection = this.FindControl<StackPanel>("TagEditorSection");
        var newTagTextBox = this.FindControl<TextBox>("NewTagTextBox");

        if (tagSection != null) tagSection.IsVisible = false;
        if (newTagTextBox != null) newTagTextBox.Text = "";

        _editingTagsGoal = null;
        HideError();
    }
    private async void OnRemoveTagClick(SavingsGoal goal, string tag)
    {
        goal.Tags.Remove(tag);
        SaveGoalsToFile();
        UpdateUI();

        ShowError($"Removed '{tag}' from {goal.Name}.", "#10b981");
        await System.Threading.Tasks.Task.Delay(2000);
        HideError();
    }
    private void ShowOptionsMenu(Button button, SavingsGoal goal)
    {
        var contextMenu = new ContextMenu();

        // Add tag option (only if goal has no tags otherwise..)
        if (goal.Tags.Count < 1)
        {
            var addTagItem = new MenuItem { Header = "Add Tag" };
            addTagItem.Click += (s, e) => OnAddTagButtonClick(goal);
            contextMenu.Items.Add(addTagItem);
        }

        // Archive/Unarchive option
        var archiveText = goal.IsArchived ? "Unarchive" : "Archive";
        var archiveItem = new MenuItem { Header = archiveText };
        archiveItem.Click += (s, e) => OnArchiveGoalClick(goal);
        contextMenu.Items.Add(archiveItem);

        contextMenu.Open(button);
    }
    private void OnArchiveButtonClick(object? sender, RoutedEventArgs e)
    {
        _showingArchivedGoals = !_showingArchivedGoals;
        var archiveButton = this.FindControl<Button>("ArchiveButton");
        var backButton = this.FindControl<Button>("BackToGoalsButton");

        if (archiveButton != null)
            archiveButton.Content = _showingArchivedGoals ? "View Active Goals" : "View Archived";
        if (backButton != null)
            backButton.IsVisible = _showingArchivedGoals;

        UpdateUI();
    }

    private void OnBackToGoalsClick(object? sender, RoutedEventArgs e)
    {
        _showingArchivedGoals = false;
        var archiveButton = this.FindControl<Button>("ArchiveButton");
        var backButton = this.FindControl<Button>("BackToGoalsButton");

        if (archiveButton != null)
            archiveButton.Content = "View Archived";
        if (backButton != null)
            backButton.IsVisible = false;

        UpdateUI();
    }

    private async void OnArchiveGoalClick(SavingsGoal goal)
    {
        goal.IsArchived = !goal.IsArchived;
        SaveGoalsToFile();
        UpdateUI();

        var action = goal.IsArchived ? "archived" : "restored";
        ShowError($"{goal.Name} has been {action}.", "#10b981");
        await System.Threading.Tasks.Task.Delay(2000);
        HideError();
    }
    #endregion

    #region UI Update Methods
    /// Update the dropdown list items to reflect current goals because why not lmfao
    private void UpdateGoalSelectorItems()
    {
        _goalDisplayItems.Clear();
        foreach (var goal in _savingsGoals.Where(g => !g.IsArchived))
        {
            var displayText = goal.IsCompleted ?
                $"{goal.Name} (Completed)" :
                $"{goal.Name} (${goal.CurrentAmount:N0} / ${goal.TargetAmount:N0})";
            _goalDisplayItems.Add(new GoalDisplayItem
            {
                DisplayText = displayText,
                Goal = goal
            });
        }
    }
    /// Master method to update all UI elements
    private void UpdateUI()
    {
        UpdateTotalSavings();
        UpdateGoalCards();
        UpdateVisibility();
        UpdateGoalSelectorItems();
        UpdateOverview();
    }
    private void UpdateTotalSavings()
    {
        var totalText = this.FindControl<TextBlock>("TotalSavingsText");
        var goalsText = this.FindControl<TextBlock>("TotalGoalsText");

        var activeGoals = _savingsGoals.Where(g => !g.IsArchived);

        if (totalText != null)
        {
            var total = activeGoals.Sum(g => g.CurrentAmount);
            totalText.Text = $"${total:N2}";
        }
        if (goalsText != null)
        {
            var count = activeGoals.Count();
            var completed = activeGoals.Count(g => g.IsCompleted);
            var active = count - completed;

            if (count == 0)
                goalsText.Text = "No active goals";
            else if (completed == 0)
                goalsText.Text = $"{active} active goal{(active == 1 ? "" : "s")}";
            else if (active == 0)
                goalsText.Text = $"{completed} completed goal{(completed == 1 ? "" : "s")}";
            else
                goalsText.Text = $"{active} active • {completed} completed";
        }
    }
    private void UpdateOverview()
    {
        var overviewPanel = this.FindControl<StackPanel>("OverviewPanel");
        if (overviewPanel == null) return;
        overviewPanel.Children.Clear();
        var activeGoals = _savingsGoals.Where(g => !g.IsArchived).ToList();
        if (activeGoals.Count == 0)
        {
            overviewPanel.Children.Add(new TextBlock { Text = "N/A" });
            return;
        }
        // Group goals by tags
        var tagGroups = _savingsGoals
            .Where(g => g.Tags.Any())
            .SelectMany(g => g.Tags.Select(tag => new { Tag = tag, Goal = g }))
            .GroupBy(x => x.Tag, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key)
            .ToList();
        var untaggedGoals = _savingsGoals.Where(g => !g.Tags.Any()).ToList();
        foreach (var tagGroup in tagGroups)
        {
            var tagTotal = tagGroup.Sum(x => x.Goal.CurrentAmount);
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var caret = new TextBlock { Text = "▶", Margin = new Thickness(0, 0, 5, 0) };
            var headerText = new TextBlock { Text = $"{tagGroup.Key}: ${tagTotal:N2}", FontWeight = Avalonia.Media.FontWeight.Bold };
            headerPanel.Children.Add(caret);
            headerPanel.Children.Add(headerText);
            var itemsPanel = new StackPanel { Margin = new Thickness(20, 0, 0, 10), IsVisible = false };

            foreach (var item in tagGroup.OrderBy(x => x.Goal.Name))
            {
                var progressInfo = item.Goal.IsCompleted ?
                    "Complete" :
                    $"{item.Goal.ProgressPercentage:F0}%";

                itemsPanel.Children.Add(new TextBlock { Text = $"• {item.Goal.Name}: ${item.Goal.CurrentAmount:N2} ({progressInfo})" });
            }
            // Toggle visibility on header click
            headerPanel.PointerPressed += (s, e) =>
            {
                itemsPanel.IsVisible = !itemsPanel.IsVisible;
                caret.Text = itemsPanel.IsVisible ? "▼" : "▶"; // Down-pointing arrow when expanded
            };

            overviewPanel.Children.Add(headerPanel);
            overviewPanel.Children.Add(itemsPanel);
        }

        // Handle untagged goals the same way if needed
        if (untaggedGoals.Any())
        {
            var untaggedTotal = untaggedGoals.Sum(g => g.CurrentAmount);
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var caret = new TextBlock { Text = "▶", Margin = new Thickness(0, 0, 5, 0) };
            var headerText = new TextBlock { Text = $"Untagged: ${untaggedTotal:N2}", FontWeight = Avalonia.Media.FontWeight.Bold };
            headerPanel.Children.Add(caret);
            headerPanel.Children.Add(headerText);
            var itemsPanel = new StackPanel { Margin = new Thickness(20, 0, 0, 10), IsVisible = false };
            foreach (var goal in untaggedGoals.OrderBy(g => g.Name))
            {
                var progressInfo = goal.IsCompleted ?
                    "Complete" :
                    $"{goal.ProgressPercentage:F0}%";

                itemsPanel.Children.Add(new TextBlock { Text = $"• {goal.Name}: ${goal.CurrentAmount:N2} ({progressInfo})" });
            }

            headerPanel.PointerPressed += (s, e) =>
            {
                itemsPanel.IsVisible = !itemsPanel.IsVisible;
                caret.Text = itemsPanel.IsVisible ? "▼" : "▶";
            };
            overviewPanel.Children.Add(headerPanel);
            overviewPanel.Children.Add(itemsPanel);
        }
        var grandTotal = _savingsGoals.Sum(g => g.CurrentAmount);
        if (_savingsGoals.Any())
        {
            overviewPanel.Children.Add(new Rectangle { Height = 1, Fill = Avalonia.Media.Brushes.Gray, Margin = new Thickness(0, 10, 0, 10) });
            overviewPanel.Children.Add(new TextBlock { Text = $"Total Savings: ${grandTotal:N2}", FontWeight = Avalonia.Media.FontWeight.Bold,
                TextAlignment = Avalonia.Media.TextAlignment.Center
            });
        }
    }
    private void UpdateGoalCards()
    {
        var goalsPanel = this.FindControl<StackPanel>("SavingsGoalsPanel");
        if (goalsPanel == null) return;
        goalsPanel.Children.Clear();
        var goalsToShow = _showingArchivedGoals
    ? _savingsGoals.Where(g => g.IsArchived)
    : _savingsGoals.Where(g => !g.IsArchived);

        foreach (var goal in goalsToShow.OrderByDescending(g => g.CreatedDate))
        {
            var card = CreateGoalCard(goal);
            goalsPanel.Children.Add(card);
        }
    }
    private void UpdateVisibility()
    {
        var emptyState = this.FindControl<StackPanel>("EmptyStatePanel");
        var scrollViewer = this.FindControl<ScrollViewer>("SavingsScrollViewer");
        var hasGoals = _savingsGoals.Count > 0;
        if (emptyState != null) emptyState.IsVisible = !hasGoals;
        if (scrollViewer != null) scrollViewer.IsVisible = hasGoals;
    }
    #endregion

    #region Goal Card Creation
    /// Create a visual card for a single savings goal dyna bro
    private Border CreateGoalCard(SavingsGoal goal)
    {
        var card = new Border
        {
            Classes = { "goal-card" }
        };
        if (_selectedGoal?.Id == goal.Id)
        {
            card.Classes.Add("selected");
        }
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        card.Child = grid;
        var leftStack = CreateGoalInfoSection(goal);
        Grid.SetColumn(leftStack, 0);
        grid.Children.Add(leftStack);
        var rightStack = CreateGoalActionsSection(goal);
        Grid.SetColumn(rightStack, 1);
        grid.Children.Add(rightStack);
        card.PointerPressed += (s, e) => OnGoalCardClick(goal);

        return card;
    }
    /// Create the left side of a goal card (name, target, progress bar okk!)
    private StackPanel CreateGoalInfoSection(SavingsGoal goal)
    {
        var leftStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Top
        };
        var nameTagsGrid = new Grid();
        nameTagsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        nameTagsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        var nameBlock = new TextBlock
        {
            Text = goal.Name,
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(nameBlock, 0);
        nameTagsGrid.Children.Add(nameBlock);
        var tagsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Avalonia.Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tagsPanel, 1);
        nameTagsGrid.Children.Add(tagsPanel);

        // Add tag badges
        foreach (var tag in goal.Tags)
        {
            var tagGrid = new Grid
            {
                Margin = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            var tagButton = new Button
            {
                Content = tagGrid,
                Classes = { "tag-button" }
            };
            tagButton.Height = 24;
            var tagText = new TextBlock
            {
                Text = tag,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(6, 2, 12, 2),
                FontSize = 10
            };
            var removeIcon = new TextBlock
            {
                Text = "×",
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 2, 4, 0),
                IsVisible = false,
                Foreground = new SolidColorBrush(Colors.White)
            };
            tagGrid.Children.Add(tagText);
            tagGrid.Children.Add(removeIcon);
            tagButton.PointerEntered += (s, e) => {
                removeIcon.IsVisible = true;
            };
            tagButton.PointerExited += (s, e) => {
                removeIcon.IsVisible = false;
            };

            tagButton.Click += (s, e) => {
                e.Handled = true;
                OnRemoveTagClick(goal, tag);
            };

            tagsPanel.Children.Add(tagButton);
        }

        // Only show add tag button if goal has fewer than 1 tag (limit per tag)
        // Always show the + button for options menu
        var optionsBtn = new Button
        {
            Content = "\uE710",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Classes = { "add-tag" },
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        optionsBtn.Click += (s, e) => {
            e.Handled = true;
            ShowOptionsMenu(optionsBtn, goal);
        };
        tagsPanel.Children.Add(optionsBtn);

        leftStack.Children.Add(nameTagsGrid);

        var targetBlock = new TextBlock
        {
            Text = $"Target: ${goal.TargetAmount:N2}",
            Foreground = new SolidColorBrush(Color.Parse("#9ca3af")),
            FontSize = 12,
            Margin = new Avalonia.Thickness(0, 4, 0, 8)
        };
        leftStack.Children.Add(targetBlock);
        var progressBar = new ProgressBar
        {
            Classes = { "modern" },
            Value = (double)goal.ProgressPercentage,
            Maximum = 100,
            Margin = new Avalonia.Thickness(0, 0, 0, 4)
        };
        leftStack.Children.Add(progressBar);
        var progressText = new TextBlock
        {
            Text = $"{goal.ProgressPercentage:F1}% complete",
            Foreground = new SolidColorBrush(Color.Parse("#6b7280")),
            FontSize = 11
        };
        leftStack.Children.Add(progressText);
        return leftStack;
    }
    //Create the right side of a goal card (current amount, status, delete button, js covering the basics nth twa)
    private StackPanel CreateGoalActionsSection(SavingsGoal goal)
    {
        var rightStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };
        var amountBlock = new TextBlock
        {
            Text = $"${goal.CurrentAmount:N2}",
            Classes = { "amount" },
            TextAlignment = TextAlignment.Right,
            Margin = new Avalonia.Thickness(0, 0, 0, 4)
        };
        rightStack.Children.Add(amountBlock);
        if (goal.IsCompleted)
        {
            var completedBlock = new TextBlock
            {
                Text = "COMPLETED",
                Foreground = new SolidColorBrush(Color.Parse("#10b981")),
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                TextAlignment = TextAlignment.Right,
                Margin = new Avalonia.Thickness(0, 0, 0, 8)
            };
            rightStack.Children.Add(completedBlock);
        }
        else
        {
            var remainingBlock = new TextBlock
            {
                Text = $"${goal.RemainingAmount:N2} remaining",
                Foreground = new SolidColorBrush(Color.Parse("#6b7280")),
                FontSize = 11,
                TextAlignment = TextAlignment.Right,
                Margin = new Avalonia.Thickness(0, 0, 0, 8)
            };
            rightStack.Children.Add(remainingBlock);
        }
        // Delete button (if you couldn't tell)
        var deleteBtn = new Button
        {
            Content = "Delete",
            Classes = { "danger" },
            HorizontalAlignment = HorizontalAlignment.Right
        };
        deleteBtn.Click += (s, e) => OnDeleteGoalClick(goal);
        rightStack.Children.Add(deleteBtn);

        return rightStack;
    }
    #endregion

    #region Error Display Methods
    /// Show an error or success message to me
    private void ShowError(string message, string color = "#ef4444")
    {
        var errorText = this.FindControl<TextBlock>("ErrorMessageText");
        if (errorText != null)
        {
            errorText.Text = message;
            errorText.Foreground = new SolidColorBrush(Color.Parse(color));
            errorText.IsVisible = true;
        }
    }
    private void HideError()
    {
        var errorText = this.FindControl<TextBlock>("ErrorMessageText");
        if (errorText != null)
        {
            errorText.IsVisible = false;
        }
    }
    #endregion
}
#region Helper Classes
// Represents an item in the goal selector dropdown
public class GoalDisplayItem
{
    public string DisplayText { get; set; } = string.Empty;
    public SavingsGoal Goal { get; set; } = null!;

    public override string ToString() => DisplayText;
}
public class SavingsGoal
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public decimal CurrentAmount { get; set; }
    public DateTime CreatedDate { get; set; }
    public ObservableCollection<string> Tags { get; set; } = new();
    public decimal ProgressPercentage => TargetAmount > 0 ? Math.Min(100, (CurrentAmount / TargetAmount) * 100) : 0;
    public bool IsCompleted => CurrentAmount >= TargetAmount;
    public decimal RemainingAmount => Math.Max(0, TargetAmount - CurrentAmount);
    public bool IsArchived { get; set; } = false;
}
public class SavingsGoalData
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public decimal CurrentAmount { get; set; }
    public DateTime CreatedDate { get; set; }
    public List<string> Tags { get; set; } = new();
    public bool IsArchived { get; set; } = false;
}
#endregion