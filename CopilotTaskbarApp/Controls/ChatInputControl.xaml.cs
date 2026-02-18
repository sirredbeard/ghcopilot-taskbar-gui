using CopilotTaskbarApp.Controls.ChatInput;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;

namespace CopilotTaskbarApp.Controls;

public sealed partial class ChatInputControl : UserControl, INotifyPropertyChanged, IDisposable
{
    const string NoModelsId = "no-models";
    private string _message = string.Empty;
    private bool disposedValue;

    public event EventHandler<MessageEventArgs>? MessageSent;
    public event EventHandler? StreamingStopRequested;
    public event EventHandler? FileSendRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<int>? RequestHistoryItem; // -1 for previous, +1 for next

    public event EventHandler<WarningEventArgs> ShowWarningRequested;

    public string Message
    {
        get
        {
            return _message;
        }

        set
        {
            if (_message != value)
            {
                _message = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Message)));
            }
        }
    }

    public ChatInputControl()
    {
        this.InitializeComponent();

        ButtonSend.IsEnabled = false; // Initially disabled
    }

    #region Event Handlers
    private void MessageInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        var hasEnoughCharsForTooltip = MessageInput.Text.Length >= 5;

        UpdateSendButtonState();
        HelpTextBlock.Visibility = hasEnoughCharsForTooltip ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateSendButtonState()
    {
        var hasText = !string.IsNullOrEmpty(MessageInput.Text);
        var hasSelectedModel = SelectedModel != null && SelectedModel.Id != NoModelsId;

        ButtonSend.IsEnabled = hasText && !IsStreaming && hasSelectedModel;
    }

    private void MessageInput_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (IsStreaming)
        {
            return; // do nothing if streaming
        }

        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            var keyState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
            if ((keyState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down)
            {
                // Allow new line when Shift+Enter is pressed
                return;
            }
            else
            {
                // Prevent default Enter behavior and handle it (e.g., send message)
                e.Handled = true;

                DoMessageSend();
            }
        }
        else if (e.Key == Windows.System.VirtualKey.Up)
        {
            e.Handled = true;
            RequestHistoryItem?.Invoke(this, -1);
        }
        // Down arrow - navigate to next command
        else if (e.Key == Windows.System.VirtualKey.Down)
        {
            e.Handled = true;
            RequestHistoryItem?.Invoke(this, 1);
        }
    }
    #endregion

    private void ButtonSend_Click(object sender, RoutedEventArgs e)
    {
        if (IsStreaming)
        {
            return;
        }

        DoMessageSend();
    }

    private void DoMessageSend()
    {
        if (SelectedModel == null || SelectedModel.Id == NoModelsId)
        {
            // No model selected or placeholder is selected - can't send message
            return;
        }

        if (!string.IsNullOrEmpty(Message) || CurrentAttachment != null)
        {
            MessageSent?.Invoke(this, new MessageEventArgs
            {
                Message = Message.Trim(),
                Model = SelectedModel.Id,
                Attachment = CurrentAttachment
            });

            // Clear after sending
            Message = string.Empty;
            CurrentAttachment = null;
        }
    }

    private void ButtonStop_Click(object sender, RoutedEventArgs e)
    {
        // Raise stop streaming event
        StreamingStopRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ButtonFile_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        CurrentAttachment = null;
    }

    private void ButtonFile_Click(object sender, RoutedEventArgs e)
    {
        FileSendRequested?.Invoke(sender, EventArgs.Empty);
    }

    private void PopulateModelSelector()
    {
        if (ModelSelector.Flyout is MenuFlyout flyout)
        {
            flyout.Items.OfType<MenuFlyoutItem>()
                .ToList().ForEach(item => item.Click -= ModelMenuItem_Click);
            flyout.Items.Clear();

            if (Models == null || Models.Count == 0)
            {
                // No models available
                var addModelItem = new MenuFlyoutItem
                {
                    Text = "No models found",
                    Tag = new ModelRecord(NoModelsId, "Add Model", "Add Model")
                };
            }
            else
            {
                // Process all models in a single loop
                foreach (var model in Models)
                {
                    var displayText = model.Name;
                    var item = new MenuFlyoutItem { Text = displayText, Tag = model };
                    item.Click += ModelMenuItem_Click;
                    flyout.Items.Add(item);
                }                
            }
        }
    }

    private void ModelMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var record = (ModelRecord)((MenuFlyoutItem)sender).Tag;
        SelectedModel = record;
    }

    private void SwitchStreaming(bool isStreaming)
    {
        // switch button visibility
        this.ButtonStop.Visibility = isStreaming ? Visibility.Visible : Visibility.Collapsed;
        this.ButtonSend.Visibility = ButtonStop.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; // isStreaming || Message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        this.ButtonFile.IsEnabled = !isStreaming;
        this.MessageInput.IsEnabled = !isStreaming;
        this.ModelSelector.IsEnabled = !isStreaming;

        // Update send button state considering the new streaming state
        UpdateSendButtonState();

        if (!isStreaming)
        {
            this.MessageInput.Focus(FocusState.Programmatic);
        }
    }

    internal async void FocusInput()
    {
        if (MessageInput.IsEnabled)
        {
            await Task.Delay(100); // Allow UI to settle
            var wasFocused = MessageInput.Focus(FocusState.Programmatic);
            Debug.Assert(wasFocused);
        }
    }

    /// <summary>
    /// Get the current cursor position in the message input
    /// </summary>
    /// <returns>The cursor position (SelectionStart)</returns>
    public int GetCursorPosition()
    {
        return MessageInput.SelectionStart;
    }

    /// <summary>
    /// Set the cursor position in the message input
    /// </summary>
    /// <param name="position">The position to set the cursor to</param>
    public void SetCursorPosition(int position)
    {
        if (position < 0) position = 0;
        if (position > (MessageInput.Text?.Length ?? 0)) position = MessageInput.Text?.Length ?? 0;

        MessageInput.SelectionStart = position;
        MessageInput.SelectionLength = 0;
    }

    private FileAttachment? _currentAttachment;
    private List<string>? _allowedFileExtensions = [];

    public FileAttachment? CurrentAttachment
    {
        get => _currentAttachment;
        set
        {
            _currentAttachment = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentAttachment)));
            UpdateFileButtonState();
        }
    }

    private void UpdateFileButtonState()
    {
        if (CurrentAttachment == null)
        {
            ButtonFile.Content = new FontIcon { Glyph = "\uE8E5", FontSize = 14 };
            ToolTipService.SetToolTip(ButtonFile, "Attach file");

            if (_allowedFileExtensions == null || _allowedFileExtensions.Count == 0)
            {
                ButtonFile.Visibility = Visibility.Collapsed;
                return;
            }
            else if (ButtonFile.Visibility == Visibility.Collapsed)
            {
                ButtonFile.Visibility = Visibility.Visible;
            }
        }
        else
        {
            // Set icon based on file type
            var icon = CurrentAttachment.FileType.ToLower() switch
            {
                ".pdf" => "\uEA90",
                ".doc" or ".docx" => "\uE8A5",
                ".jpg" or ".png" or ".gif" => "\uE91B",
                _ => "\uE8A5"  // default document icon
            };

            ButtonFile.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
            {
                new FontIcon { Glyph = icon, FontSize = 14 },
                new TextBlock
                {
                    Text = CurrentAttachment.FileName,
                    Margin = new Thickness(4,0,0,0),
                    MaxWidth = 80,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            }
            };

            ToolTipService.SetToolTip(ButtonFile, $"{CurrentAttachment.FileName} ({CurrentAttachment.FileSize / 1024:N0} KB)");
        }
    }

    #region DP
    public bool IsStreaming
    {
        get => (bool)GetValue(IsStreamingProperty);
        set => SetValue(IsStreamingProperty, value);
    }

    public static readonly DependencyProperty IsStreamingProperty =
        DependencyProperty.Register(nameof(IsStreaming), typeof(bool), typeof(ChatInputControl),
            new PropertyMetadata(false, OnIsStreamingChanged));

    private static void OnIsStreamingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatInputControl control)
        {
            control.SwitchStreaming((bool)e.NewValue);
        }
    }

    public ObservableCollection<ModelRecord> Models
    {
        get { return (ObservableCollection<ModelRecord>)GetValue(ModelsProperty); }
        set { SetValue(ModelsProperty, value); }
    }

    // Using a DependencyProperty as the backing store for Models.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty ModelsProperty =
        DependencyProperty.Register("Models", typeof(ObservableCollection<ModelRecord>), typeof(ChatInputControl), new PropertyMetadata(
            new ObservableCollection<ModelRecord>(), OnModelsSet));

    private static void OnModelsSet(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatInputControl control)
        {
            control.PopulateModelSelector();

            if (control.Models == null || control.Models.Count == 0)
            {
                // No models available - select placeholder
                control.SelectedModel = new ModelRecord(NoModelsId, "Select Model", "Select Model");
            }
            else if (control.SelectedModel == null || control.SelectedModel.Id == NoModelsId)
            {
                // Models are available and either no model is selected or placeholder was selected
                control.SelectedModel = control.Models[0];
            }

            control.Models.CollectionChanged -= control.Models_CollectionChanged;
            control.Models.CollectionChanged += control.Models_CollectionChanged;
        }
    }

    private void Models_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        PopulateModelSelector();
        
        // Handle model selection when collection changes
        if (Models == null || Models.Count == 0)
        {
            // No models available - select placeholder
            SelectedModel = new ModelRecord(NoModelsId, "Select Model", "Select Model");
        }
        else if (SelectedModel == null || SelectedModel.Id == NoModelsId)
        {
            // Models are available and either no model is selected or placeholder was selected
            SelectedModel = Models[0];
        }
        else if (!string.IsNullOrEmpty(SelectedModel.Id) && !Models.Any(m => m.Id == SelectedModel.Id))
        {
            // The currently selected model was removed from the collection
            SelectedModel = Models[0];
        }
    }

    public ModelRecord SelectedModel
    {
        get { return (ModelRecord)GetValue(SelectedModelProperty); }
        set { SetValue(SelectedModelProperty, value); }
    }

    public List<string>? AllowedFileExtensions
    {
        get => _allowedFileExtensions;
        set
        {
            _allowedFileExtensions = value;
            UpdateFileButtonState();
        }
    }

    public bool ReadOnly { get; internal set; }

    public static readonly DependencyProperty SelectedModelProperty =
        DependencyProperty.Register("SelectedModel", typeof(ModelRecord), typeof(ChatInputControl),
            new PropertyMetadata(default(ModelRecord), OnSelectedModelSet));

    private static void OnSelectedModelSet(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatInputControl control)
        {
            if (e.NewValue is ModelRecord model)
            {
                if (model.Id == NoModelsId)
                {
                    control.ModelSelector.Content = "Select Model...";
                }
                else
                {
                    control.ModelSelector.Content = model.Name;
                }
            }
            else
            {
                // No model selected - show placeholder
                control.ModelSelector.Content = "Select Model...";
            }

            // Update button enabled state when model selection changes
            control.UpdateSendButtonState();
        }
    }
    #endregion

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // Dispose managed resources
                Models.CollectionChanged -= Models_CollectionChanged;
            }

            disposedValue=true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private async Task<bool> ValidateFileAsync(StorageFile file)
    {
        var properties = await file.GetBasicPropertiesAsync();

        // Example size limit of 50MB
        const long MAX_FILE_SIZE = 50 * 1024 * 1024;

        if (properties.Size > MAX_FILE_SIZE)
        {
            // Show error
            return false;
        }

        // Use FileTypesHelpers to check allowed file types
        var ext = file.FileType.ToLowerInvariant();
        if (!FileTypesHelpers.IsSupportedFileExtension(ext))
        {
            // Show error
            return false;
        }

        return true;
    }

    private void MessageInput_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;

        // Show visual feedback if it's a file
        //e.DragUIOverride.Caption = "Release to attach file";
        e.DragUIOverride.IsContentVisible = true;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.IsCaptionVisible = false;
    }

    private async void MessageInput_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var file = items.FirstOrDefault() as StorageFile;

            if (file != null && await ValidateFileAsync(file))
            {
                var properties = await file.GetBasicPropertiesAsync();

                CurrentAttachment = new FileAttachment
                {
                    FilePath = file.Path,
                    FileName = file.Name,
                    FileType = file.FileType,
                    FileSize = (long)properties.Size
                };
            }
            else
            {
                ShowWarningRequested?.Invoke(this, new WarningEventArgs("Invalid file",
                    "The file you are trying to attach is not supported or exceeds the size limit."));
            }

        }
    }

    private async void MessageInput_Paste(object sender, TextControlPasteEventArgs e)
    {
        var dataPackage = Clipboard.GetContent();
        bool handled = false;

        // First, check for files
        if (dataPackage.Contains(StandardDataFormats.StorageItems))
        {
            handled = true;
            await HandleStorageItemsPaste(dataPackage);
        }
        // Then check for images
        else if (dataPackage.Contains(StandardDataFormats.Bitmap))
        {
            handled = true;
            await HandleImagePaste(dataPackage);
        }

        if (handled)
        {
            e.Handled = true;
        }
    }

    private async Task HandleStorageItemsPaste(DataPackageView dataPackage)
    {
        try
        {
            var items = await dataPackage.GetStorageItemsAsync();
            if (items.Count > 0)
            {
                // For simplicity, just use the first file
                var file = items[0] as StorageFile;
                if (file != null)
                {
                    var properties = await file.GetBasicPropertiesAsync();

                    CurrentAttachment = new FileAttachment
                    {
                        FilePath = file.Path,
                        FileName = file.Name,
                        FileType = file.FileType,
                        FileSize = (long)properties.Size
                    };
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to handle file paste: {ex}");
            ShowAttachmentError("Failed to attach file from clipboard");
        }
    }

    private async Task HandleImagePaste(DataPackageView dataPackage)
    {
        try
        {
            var imageStream = await dataPackage.GetBitmapAsync();
            if (imageStream != null)
            {
                // Generate unique filename in temp folder
                string fileName = $"clipboard_image_{DateTime.Now:yyyyMMddHHmmss}.png";
                string tempPath = Path.Combine(Path.GetTempPath(), fileName);

                // Open the stream from clipboard
                using var randomAccessStream = await imageStream.OpenReadAsync();
                // Create decoder to read the image
                var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);

                // Check dimensions
                uint originalWidth = decoder.PixelWidth;
                uint originalHeight = decoder.PixelHeight;

                // Maximum dimensions allowed
                const uint MAX_DIMENSION = 1568;

                // Determine if resizing is needed and calculate new dimensions
                uint newWidth = originalWidth;
                uint newHeight = originalHeight;

                if (originalWidth > MAX_DIMENSION || originalHeight > MAX_DIMENSION)
                {
                    if (originalWidth > originalHeight)
                    {
                        // Landscape
                        newWidth = MAX_DIMENSION;
                        newHeight = (uint)(originalHeight * (MAX_DIMENSION / (double)originalWidth));
                    }
                    else
                    {
                        // Portrait
                        newHeight = MAX_DIMENSION;
                        newWidth = (uint)(originalWidth * (MAX_DIMENSION / (double)originalHeight));
                    }
                }

                // Create file using System.IO
                using (var fileStream = new FileStream(tempPath, FileMode.Create))
                {
                    // Convert to Windows.Storage.Streams.IRandomAccessStream
                    var outputStream = fileStream.AsRandomAccessStream();

                    // Create encoder for output
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outputStream);

                    // Set the size
                    encoder.BitmapTransform.ScaledWidth = newWidth;
                    encoder.BitmapTransform.ScaledHeight = newHeight;
                    encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Linear;

                    // Get pixel data from decoder
                    var pixelData = await decoder.GetPixelDataAsync(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied,
                        new BitmapTransform(),
                        ExifOrientationMode.RespectExifOrientation,
                        ColorManagementMode.DoNotColorManage);

                    // Set pixels to encoder
                    encoder.SetPixelData(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied,
                        newWidth,
                        newHeight,
                        decoder.DpiX,
                        decoder.DpiY,
                        pixelData.DetachPixelData());

                    // Save the image
                    await encoder.FlushAsync();
                }

                // Create attachment
                var fileInfo = new FileInfo(tempPath);
                CurrentAttachment = new FileAttachment
                {
                    FilePath = tempPath,
                    FileName = fileName,
                    FileType = ".png",
                    FileSize = fileInfo.Length,
                };
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to handle image paste: {ex}");
            ShowAttachmentError("Failed to attach image from clipboard");
        }
    }

    private void ShowAttachmentError(string message)
    {
        ShowWarningRequested?.Invoke(this, new WarningEventArgs("Attachment Error", message));
    }

    private void MessageInput_DragEnter(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            DropOverlay.Visibility = Visibility.Visible;
        }
    }

    private void MessageInput_DragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }
}
