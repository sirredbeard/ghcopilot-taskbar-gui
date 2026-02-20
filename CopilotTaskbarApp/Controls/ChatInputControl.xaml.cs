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
    private const string NoModelsId = "no-models";

    private string _message = string.Empty;
    private bool _disposed;
    private FileAttachment? _currentAttachment;
    private List<string>? _allowedFileExtensions = [];

    #region Events

    public event EventHandler<MessageEventArgs>? MessageSent;
    public event EventHandler? StreamingStopRequested;
    public event EventHandler? FileSendRequested;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<int>? RequestHistoryItem; // -1 for previous, +1 for next
    public event EventHandler<WarningEventArgs>? ShowWarningRequested;

    #endregion

    #region Properties

    public string Message
    {
        get => _message;
        set
        {
            if (_message != value)
            {
                _message = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Message)));
            }
        }
    }

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

    #endregion

    #region Dependency Properties

    public bool IsStreaming
    {
        get => (bool)GetValue(IsStreamingProperty);
        set => SetValue(IsStreamingProperty, value);
    }

    public static readonly DependencyProperty IsStreamingProperty =
        DependencyProperty.Register(nameof(IsStreaming), typeof(bool), typeof(ChatInputControl),
            new PropertyMetadata(false, OnIsStreamingChanged));

    public ObservableCollection<ModelRecord> Models
    {
        get => (ObservableCollection<ModelRecord>)GetValue(ModelsProperty);
        set => SetValue(ModelsProperty, value);
    }

    public static readonly DependencyProperty ModelsProperty =
        DependencyProperty.Register(nameof(Models), typeof(ObservableCollection<ModelRecord>), typeof(ChatInputControl),
            new PropertyMetadata(new ObservableCollection<ModelRecord>(), OnModelsSet));

    public ModelRecord SelectedModel
    {
        get => (ModelRecord)GetValue(SelectedModelProperty);
        set => SetValue(SelectedModelProperty, value);
    }

    public static readonly DependencyProperty SelectedModelProperty =
        DependencyProperty.Register(nameof(SelectedModel), typeof(ModelRecord), typeof(ChatInputControl),
            new PropertyMetadata(default(ModelRecord), OnSelectedModelSet));

    #endregion

    public ChatInputControl()
    {
        InitializeComponent();
        ButtonSend.IsEnabled = false;
    }

    #region Public Methods

    public int GetCursorPosition() => MessageInput.SelectionStart;

    public void SetCursorPosition(int position)
    {
        position = Math.Clamp(position, 0, MessageInput.Text?.Length ?? 0);
        MessageInput.SelectionStart = position;
        MessageInput.SelectionLength = 0;
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

    #endregion

    #region Input Event Handlers

    private void MessageInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        var hasEnoughCharsForTooltip = MessageInput.Text.Length >= 5;

        UpdateSendButtonState();
        HelpTextBlock.Visibility = hasEnoughCharsForTooltip ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MessageInput_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (IsStreaming)
            return;

        if (e.Key == VirtualKey.Enter)
        {
            var keyState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
            if ((keyState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down)
                return; // Allow new line with Shift+Enter

            e.Handled = true;
            DoMessageSend();
        }
        else if (e.Key == VirtualKey.Up)
        {
            e.Handled = true;
            RequestHistoryItem?.Invoke(this, -1);
        }
        else if (e.Key == VirtualKey.Down)
        {
            e.Handled = true;
            RequestHistoryItem?.Invoke(this, 1);
        }
    }

    #endregion

    #region Button Handlers

    private void ButtonSend_Click(object sender, RoutedEventArgs e)
    {
        if (!IsStreaming)
            DoMessageSend();
    }

    private void ButtonStop_Click(object sender, RoutedEventArgs e)
    {
        StreamingStopRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ButtonFile_Click(object sender, RoutedEventArgs e)
    {
        FileSendRequested?.Invoke(sender, EventArgs.Empty);
    }

    private void ButtonFile_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        CurrentAttachment = null;
    }

    #endregion

    #region Drag & Drop

    private void MessageInput_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.IsContentVisible = true;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.IsCaptionVisible = false;
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

    private async void MessageInput_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;

        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        var items = await e.DataView.GetStorageItemsAsync();
        var file = items.FirstOrDefault() as StorageFile;

        if (file != null && await ValidateFileAsync(file))
        {
            var properties = await file.GetBasicPropertiesAsync();
            CurrentAttachment = new FileAttachment(file.Path, file.Name, file.FileType, (long)properties.Size);
        }
        else
        {
            ShowWarningRequested?.Invoke(this, new WarningEventArgs("Invalid file",
                "The file you are trying to attach is not supported or exceeds the size limit."));
        }
    }

    #endregion

    #region Clipboard / Paste

    private async void MessageInput_Paste(object sender, TextControlPasteEventArgs e)
    {
        var dataPackage = Clipboard.GetContent();

        if (dataPackage.Contains(StandardDataFormats.StorageItems))
        {
            e.Handled = true;
            await HandleStorageItemsPaste(dataPackage);
        }
        else if (dataPackage.Contains(StandardDataFormats.Bitmap))
        {
            e.Handled = true;
            await HandleImagePaste(dataPackage);
        }
    }

    private async Task HandleStorageItemsPaste(DataPackageView dataPackage)
    {
        try
        {
            var items = await dataPackage.GetStorageItemsAsync();
            if (items.Count == 0)
                return;

            var file = items[0] as StorageFile;
            if (file == null)
                return;

            var properties = await file.GetBasicPropertiesAsync();
            CurrentAttachment = new FileAttachment(file.Path, file.Name, file.FileType, (long)properties.Size);
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
            if (imageStream == null)
                return;

            string fileName = $"clipboard_image_{DateTime.Now:yyyyMMddHHmmss}.png";
            string tempPath = Path.Combine(Path.GetTempPath(), fileName);

            using var randomAccessStream = await imageStream.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);

            uint originalWidth = decoder.PixelWidth;
            uint originalHeight = decoder.PixelHeight;
            const uint MAX_DIMENSION = 1568;

            uint newWidth = originalWidth;
            uint newHeight = originalHeight;

            if (originalWidth > MAX_DIMENSION || originalHeight > MAX_DIMENSION)
            {
                if (originalWidth > originalHeight)
                {
                    newWidth = MAX_DIMENSION;
                    newHeight = (uint)(originalHeight * (MAX_DIMENSION / (double)originalWidth));
                }
                else
                {
                    newHeight = MAX_DIMENSION;
                    newWidth = (uint)(originalWidth * (MAX_DIMENSION / (double)originalHeight));
                }
            }

            using (var fileStream = new FileStream(tempPath, FileMode.Create))
            {
                var outputStream = fileStream.AsRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outputStream);

                encoder.BitmapTransform.ScaledWidth = newWidth;
                encoder.BitmapTransform.ScaledHeight = newHeight;
                encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Linear;

                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    new BitmapTransform(),
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    newWidth,
                    newHeight,
                    decoder.DpiX,
                    decoder.DpiY,
                    pixelData.DetachPixelData());

                await encoder.FlushAsync();
            }

            var fileInfo = new FileInfo(tempPath);
            CurrentAttachment = new FileAttachment(tempPath, fileName, ".png", fileInfo.Length);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to handle image paste: {ex}");
            ShowAttachmentError("Failed to attach image from clipboard");
        }
    }

    #endregion

    #region Model Selector

    private void PopulateModelSelector()
    {
        if (ModelSelector.Flyout is not MenuFlyout flyout)
            return;

        flyout.Items.OfType<MenuFlyoutItem>()
            .ToList().ForEach(item => item.Click -= ModelMenuItem_Click);
        flyout.Items.Clear();

        if (Models == null || Models.Count == 0)
        {
            var addModelItem = new MenuFlyoutItem
            {
                Text = "No models found",
                Tag = new ModelRecord(NoModelsId, "Add Model", "Add Model")
            };
        }
        else
        {
            foreach (var model in Models)
            {
                var item = new MenuFlyoutItem { Text = model.Name, Tag = model };
                item.Click += ModelMenuItem_Click;
                flyout.Items.Add(item);
            }
        }
    }

    private void ModelMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var record = (ModelRecord)((MenuFlyoutItem)sender).Tag;
        SelectedModel = record;
    }

    #endregion

    #region Dependency Property Callbacks

    private static void OnIsStreamingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatInputControl control)
            control.SwitchStreaming((bool)e.NewValue);
    }

    private static void OnModelsSet(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ChatInputControl control)
            return;

        control.PopulateModelSelector();

        if (control.Models == null || control.Models.Count == 0)
        {
            control.SelectedModel = new ModelRecord(NoModelsId, "Select Model", "Select Model");
        }
        else if (control.SelectedModel == null || control.SelectedModel.Id == NoModelsId)
        {
            control.SelectedModel = control.Models[0];
        }

        control.Models.CollectionChanged -= control.Models_CollectionChanged;
        control.Models.CollectionChanged += control.Models_CollectionChanged;
    }

    private static void OnSelectedModelSet(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ChatInputControl control)
            return;

        if (e.NewValue is ModelRecord model)
            control.ModelSelector.Content = model.Id == NoModelsId ? "Select Model..." : model.Name;
        else
            control.ModelSelector.Content = "Select Model...";

        control.UpdateSendButtonState();
    }

    private void Models_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        PopulateModelSelector();

        if (Models == null || Models.Count == 0)
        {
            SelectedModel = new ModelRecord(NoModelsId, "Select Model", "Select Model");
        }
        else if (SelectedModel == null || SelectedModel.Id == NoModelsId)
        {
            SelectedModel = Models[0];
        }
        else if (!string.IsNullOrEmpty(SelectedModel.Id) && !Models.Any(m => m.Id == SelectedModel.Id))
        {
            SelectedModel = Models[0];
        }
    }

    #endregion

    #region Private Helpers

    private void DoMessageSend()
    {
        if (SelectedModel == null || SelectedModel.Id == NoModelsId)
            return;

        if (string.IsNullOrEmpty(Message) && CurrentAttachment == null)
            return;

        MessageSent?.Invoke(this, new MessageEventArgs
        {
            Message = Message.Trim(),
            Model = SelectedModel.Id,
            Attachment = CurrentAttachment
        });

        Message = string.Empty;
        CurrentAttachment = null;
    }

    private void UpdateSendButtonState()
    {
        var hasText = !string.IsNullOrEmpty(MessageInput.Text);
        var hasSelectedModel = SelectedModel != null && SelectedModel.Id != NoModelsId;
        ButtonSend.IsEnabled = hasText && !IsStreaming && hasSelectedModel;
    }

    private void SwitchStreaming(bool isStreaming)
    {
        ButtonStop.Visibility = isStreaming ? Visibility.Visible : Visibility.Collapsed;
        ButtonSend.Visibility = isStreaming ? Visibility.Collapsed : Visibility.Visible;

        ButtonFile.IsEnabled = !isStreaming;
        MessageInput.IsEnabled = !isStreaming;
        ModelSelector.IsEnabled = !isStreaming;

        UpdateSendButtonState();

        if (!isStreaming)
            MessageInput.Focus(FocusState.Programmatic);
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
            var icon = CurrentAttachment.FileType.ToLower() switch
            {
                ".pdf" => "\uEA90",
                ".doc" or ".docx" => "\uE8A5",
                ".jpg" or ".png" or ".gif" => "\uE91B",
                _ => "\uE8A5"
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
                        Margin = new Thickness(4, 0, 0, 0),
                        MaxWidth = 80,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                }
            };

            ToolTipService.SetToolTip(ButtonFile, $"{CurrentAttachment.FileName} ({CurrentAttachment.FileSize / 1024:N0} KB)");
        }
    }

    private async Task<bool> ValidateFileAsync(StorageFile file)
    {
        var properties = await file.GetBasicPropertiesAsync();
        const long MAX_FILE_SIZE = 50 * 1024 * 1024;

        if (properties.Size > MAX_FILE_SIZE)
            return false;

        var ext = file.FileType.ToLowerInvariant();
        if (!FileTypesHelpers.IsSupportedFileExtension(ext))
            return false;

        return true;
    }

    private void ShowAttachmentError(string message)
    {
        ShowWarningRequested?.Invoke(this, new WarningEventArgs("Attachment Error", message));
    }

    #endregion

    #region IDisposable

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                Models.CollectionChanged -= Models_CollectionChanged;
            }

            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    #endregion
}
