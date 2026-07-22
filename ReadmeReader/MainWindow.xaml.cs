using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ReadmeReader;

public partial class MainWindow : Window
{
    private string? currentFile;
    private string currentMarkdown = string.Empty;
    private bool isDarkMode;
    private bool isFullScreen;
    private WindowStyle previousWindowStyle;
    private WindowState previousWindowState;
    private ResizeMode previousResizeMode;
    private Thickness previousDocumentMargin;
    private readonly List<TextRange> searchMatches = [];
    private string activeSearchTerm = string.Empty;
    private int currentSearchIndex = -1;
    private bool hasUnsavedChanges;
    private string selectedHighlightColor = "#FFF3A3";
    private MarkupTool activeMarkupTool = MarkupTool.None;
    private readonly UserSettings userSettings = UserSettings.Load();
    private bool isUpdatingRecentFiles;
    private double lastRenderViewportWidth;
    private double smoothScrollTarget;
    private bool isSmoothScrolling;
    private double readingZoom = 100;
    private bool isUpdatingZoomScale;

    public MainWindow(string? initialFile)
    {
        InitializeComponent();
        Viewer.SizeChanged += Viewer_SizeChanged;
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, (_, _) => OpenMarkdownFile()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, _) => Save()));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Open, Key.O, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Save, Key.S, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => SaveAs()), Key.S, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => FocusSearch()), Key.F, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => AdjustZoom(10)), Key.OemPlus, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => AdjustZoom(10)), Key.Add, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => AdjustZoom(-10)), Key.OemMinus, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => AdjustZoom(-10)), Key.Subtract, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => SetZoom(100)), Key.D0, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => ToggleDarkMode()), Key.D, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => ToggleHighlightTool()), Key.H, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => ToggleEraseTool()), Key.H, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => GoToPreviousMatch()), Key.F3, ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => GoToNextMatch()), Key.F3, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => ToggleFullScreen()), Key.F11, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => HandleEscape()), Key.Escape, ModifierKeys.None));
        Closing += (_, e) =>
        {
            if (!ConfirmDiscardUnsavedChanges())
            {
                e.Cancel = true;
                return;
            }

            SaveCurrentSettings();
        };

        Width = userSettings.WindowWidth;
        Height = userSettings.WindowHeight;
        isDarkMode = false;
        userSettings.IsDarkMode = false;
        ThemeToggle.IsChecked = isDarkMode;
        RefreshRecentFiles();

        if (!string.IsNullOrWhiteSpace(initialFile))
        {
            LoadMarkdown(initialFile);
        }
        else
        {
            ShowWelcomeDocument();
        }

        SetZoom(userSettings.Zoom);
        ApplyChromeTheme();
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e) => OpenMarkdownFile();

    private void SaveButton_Click(object sender, RoutedEventArgs e) => Save();

    private void SaveAsButton_Click(object sender, RoutedEventArgs e) => SaveAs();

    private void RecentFilesBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isUpdatingRecentFiles || RecentFilesBox.SelectedItem is not ComboBoxItem { Tag: string fileName })
        {
            return;
        }

        if (!ConfirmDiscardUnsavedChanges())
        {
            RefreshRecentFiles();
            return;
        }

        if (!DocumentFileService.Exists(fileName))
        {
            userSettings.RemoveRecentFile(fileName);
            userSettings.Save();
            RefreshRecentFiles();
            MessageBox.Show(
                $"Ya no encontre este archivo reciente:\n{fileName}",
                "Archivo reciente no disponible",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        LoadMarkdown(fileName);
    }

    private void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        if (Viewer.Document is null)
        {
            return;
        }

        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() == true)
        {
            printDialog.PrintDocument(((IDocumentPaginatorSource)Viewer.Document).DocumentPaginator, Title);
        }
    }

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => AdjustZoom(-10);

    private void ZoomInButton_Click(object sender, RoutedEventArgs e) => AdjustZoom(10);

    private void ZoomResetButton_Click(object sender, RoutedEventArgs e) => SetZoom(100);

    private void ThemeToggle_Changed(object sender, RoutedEventArgs e)
    {
        var requestedDarkMode = ThemeToggle.IsChecked == true;
        if (requestedDarkMode == isDarkMode)
        {
            return;
        }

        isDarkMode = requestedDarkMode;
        RenderCurrentDocument(preserveScroll: true);
        ApplyChromeTheme();
        userSettings.IsDarkMode = isDarkMode;
        userSettings.Save();
    }

    private void FullScreenButton_Click(object sender, RoutedEventArgs e) => ToggleFullScreen();

    private void HighlightButton_Click(object sender, RoutedEventArgs e) => ToggleHighlightTool();

    private void RemoveHighlightButton_Click(object sender, RoutedEventArgs e) => ToggleEraseTool();

    private void HighlightColor_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string color })
        {
            selectedHighlightColor = color;
        }
    }

    private void ToggleHighlightTool()
    {
        activeMarkupTool = activeMarkupTool == MarkupTool.Highlight ? MarkupTool.None : MarkupTool.Highlight;
        ApplyMarkupToolState();

        if (activeMarkupTool == MarkupTool.Highlight && !string.IsNullOrWhiteSpace(GetSelectedText()))
        {
            ApplyHighlightToSelection();
        }
    }

    private void ToggleEraseTool()
    {
        activeMarkupTool = activeMarkupTool == MarkupTool.Erase ? MarkupTool.None : MarkupTool.Erase;
        ApplyMarkupToolState();

        if (activeMarkupTool == MarkupTool.Erase && !string.IsNullOrWhiteSpace(GetSelectedText()))
        {
            RemoveHighlightFromSelection();
        }
    }

    private void ApplyActiveMarkupTool()
    {
        if (activeMarkupTool == MarkupTool.Highlight)
        {
            ApplyHighlightToSelection();
            return;
        }

        if (activeMarkupTool == MarkupTool.Erase)
        {
            RemoveHighlightFromSelection();
        }
    }

    private void ClearMarkupTool()
    {
        activeMarkupTool = MarkupTool.None;
        ApplyMarkupToolState();
    }

    private void ApplyMarkupToolState()
    {
        var activeBackground = Brush(isDarkMode ? "#4B3F20" : "#FEF3C7");
        var activeBorder = Brush(isDarkMode ? "#B8892F" : "#D97706");
        var activeText = Brush(isDarkMode ? "#F8FAFC" : "#111827");
        var buttonBackground = Brush(isDarkMode ? "#24221F" : "#FFFFFF");
        var buttonBorder = Brush(isDarkMode ? "#48443D" : "#CBD5E1");
        var buttonText = Brush(isDarkMode ? "#ECE7DE" : "#1F2937");

        SetToolButtonState(HighlightButton, activeMarkupTool == MarkupTool.Highlight, activeBackground, activeBorder, activeText, buttonBackground, buttonBorder, buttonText);
        SetToolButtonState(RemoveHighlightButton, activeMarkupTool == MarkupTool.Erase, activeBackground, activeBorder, activeText, buttonBackground, buttonBorder, buttonText);

        Viewer.Cursor = activeMarkupTool == MarkupTool.None ? Cursors.IBeam : Cursors.Cross;
        if (activeMarkupTool == MarkupTool.Highlight)
        {
            StatusText.Text = "Modo resaltador activo. Selecciona texto para resaltarlo. Esc para salir.";
        }
        else if (activeMarkupTool == MarkupTool.Erase)
        {
            StatusText.Text = "Modo goma activo. Selecciona un resaltado para quitarlo. Esc para salir.";
        }
    }

    private static void SetToolButtonState(
        Button button,
        bool isActive,
        Brush activeBackground,
        Brush activeBorder,
        Brush activeText,
        Brush buttonBackground,
        Brush buttonBorder,
        Brush buttonText)
    {
        button.Background = isActive ? activeBackground : buttonBackground;
        button.BorderBrush = isActive ? activeBorder : buttonBorder;
        button.Foreground = isActive ? activeText : buttonText;
        button.BorderThickness = isActive ? new Thickness(2) : new Thickness(1);
    }

    private void ZoomScaleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isUpdatingZoomScale || Viewer is null || ZoomScaleBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        if (double.TryParse(item.Tag?.ToString(), out var zoom))
        {
            SetZoom(zoom);
        }
    }

    private void ComboBox_DropDownOpened(object sender, EventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            ApplyCurrentComboBoxTheme(comboBox);
        }
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (SearchPanel.Visibility != Visibility.Visible)
        {
            ShowSearchPanel(focusSearch: true);
            return;
        }

        StartSearch(moveToFirstMatch: true);
    }

    private void PreviousMatchButton_Click(object sender, RoutedEventArgs e) => GoToPreviousMatch();

    private void NextMatchButton_Click(object sender, RoutedEventArgs e) => GoToNextMatch();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SearchBox.Text) && !string.IsNullOrEmpty(activeSearchTerm))
        {
            activeSearchTerm = string.Empty;
            currentSearchIndex = -1;
            RenderCurrentDocument(preserveScroll: true);
            UpdateSearchUi();
        }

        UpdateSearchUi();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (string.Equals(SearchBox.Text.Trim(), activeSearchTerm, StringComparison.CurrentCultureIgnoreCase))
            {
                GoToNextMatch();
            }
            else
            {
                StartSearch(moveToFirstMatch: true);
            }

            e.Handled = true;
        }
    }

    private void Viewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Viewer.Document is null || Math.Abs(e.NewSize.Width - lastRenderViewportWidth) < 48)
        {
            return;
        }

        RenderCurrentDocument(preserveScroll: true);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasSupportedDocumentDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!HasSupportedDocumentDrop(e))
        {
            return;
        }

        if (!ConfirmDiscardUnsavedChanges())
        {
            return;
        }

        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var file = DocumentFileService.FirstSupportedDocument(files);
        if (file is not null)
        {
            LoadMarkdown(file);
        }
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            AdjustZoom(e.Delta > 0 ? 10 : -10);
            e.Handled = true;
            return;
        }

        SmoothScrollBy(-e.Delta * 0.95);
        e.Handled = true;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!isFullScreen)
        {
            return;
        }

        var position = e.GetPosition(this);
        FloatingMarkupToolbar.Visibility = position.Y <= 84 || FloatingMarkupToolbar.IsMouseOver
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void FloatingMarkupToolbar_MouseEnter(object sender, MouseEventArgs e)
    {
        if (isFullScreen)
        {
            FloatingMarkupToolbar.Visibility = Visibility.Visible;
        }
    }

    private void FloatingMarkupToolbar_MouseLeave(object sender, MouseEventArgs e)
    {
        if (isFullScreen)
        {
            FloatingMarkupToolbar.Visibility = Visibility.Collapsed;
        }
    }

    private void Viewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (activeMarkupTool == MarkupTool.None || string.IsNullOrWhiteSpace(GetSelectedText()))
        {
            return;
        }

        ApplyActiveMarkupTool();
    }

    private void OpenMarkdownFile()
    {
        if (!ConfirmDiscardUnsavedChanges())
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = DocumentFileService.OpenFilter,
            Title = "Abrir Markdown"
        };

        if (dialog.ShowDialog(this) == true)
        {
            LoadMarkdown(dialog.FileName);
        }
    }

    private void LoadMarkdown(string fileName)
    {
        try
        {
            var markdown = DocumentFileService.Read(fileName);
            currentFile = fileName;
            currentMarkdown = markdown;
            hasUnsavedChanges = false;
            activeSearchTerm = string.Empty;
            currentSearchIndex = -1;
            SearchBox.Text = string.Empty;
            RenderCurrentDocument();
            UpdateTitle();
            StatusText.Text = fileName;
            RememberRecentFile(fileName);
        }
        catch (Exception ex)
        {
            ShowFileOperationError("No se pudo abrir el archivo", fileName, ex);
        }
    }

    private void ShowWelcomeDocument()
    {
        currentMarkdown = """
        # markit

        Abri un archivo Markdown para leerlo con formato, como si fuera un documento.

        - Usa **Abrir** para elegir un `.md`.
        - Arrastra un archivo Markdown sobre la ventana.
        - Ajusta el zoom con escalas fijas, botones, Ctrl + rueda o Ctrl + 0.
        - Cambia entre modo claro y oscuro con el interruptor de tema o Ctrl + D.
        - Busca texto con resaltado, Enter para avanzar y Shift + F3 para volver.
        - Ctrl + H activa el resaltador; Ctrl + Shift + H activa la goma; Esc sale de la herramienta.
        - Usa pantalla completa con F11 para leer sin distracciones.
        - Imprime cuando quieras leerlo como PDF.

        ```text
        Tambien podes abrir un archivo desde la consola:
        ReadmeReader.exe C:\ruta\README.md
        ```
        """;
        currentFile = null;
        hasUnsavedChanges = false;
        RenderCurrentDocument();
        UpdateTitle();
        StatusText.Text = "Listo para abrir archivos Markdown";
    }

    private void ApplyHighlightToSelection()
    {
        var selectedText = GetSelectedText();
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            MessageBox.Show("Selecciona el texto que queres resaltar.", "Sin seleccion", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var color = selectedHighlightColor;
        if (!TryFindSelectedMarkdownRange(selectedText, out var start, out var length))
        {
            MessageBox.Show(
                "No pude ubicar esa seleccion en el Markdown original sin arriesgarme a modificar otra parte. Proba seleccionando una frase mas corta o texto sin formato.",
                "No se pudo resaltar",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (IsInsideExistingMark(start))
        {
            MessageBox.Show("Esa seleccion ya parece estar dentro de un resaltado.", "Resaltado existente", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var original = currentMarkdown.Substring(start, length);
        currentMarkdown = currentMarkdown.Remove(start, length)
            .Insert(start, $"""<mark style="background-color: {color};">{original}</mark>""");
        MarkDocumentChanged("Resaltado aplicado");
    }

    private void RemoveHighlightFromSelection()
    {
        var selectedText = GetSelectedText();
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            MessageBox.Show("Selecciona texto resaltado para quitarle el color.", "Sin seleccion", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!TryFindMarksIntersectingSelection(selectedText, out var marksToRemove))
        {
            StatusText.Text = "No habia resaltados en la seleccion.";
            return;
        }

        foreach (var mark in marksToRemove.OrderByDescending(mark => mark.Start))
        {
            currentMarkdown = currentMarkdown.Remove(mark.Start, mark.Length).Insert(mark.Start, mark.InnerMarkdown);
        }

        MarkDocumentChanged(marksToRemove.Count == 1 ? "Resaltado quitado" : "Resaltados quitados");
    }

    private string GetSelectedText()
    {
        return Viewer.Selection is null || Viewer.Selection.IsEmpty
            ? string.Empty
            : Viewer.Selection.Text.Trim();
    }

    private bool TryFindSelectedMarkdownRange(string selectedText, out int start, out int length)
    {
        start = -1;
        length = 0;

        var normalizedSelection = NormalizeSelectedText(selectedText);
        if (string.IsNullOrWhiteSpace(normalizedSelection))
        {
            return false;
        }

        var visibleMap = BuildVisibleMarkdownMap(currentMarkdown);
        var visibleSelection = NormalizeForVisibleSearch(normalizedSelection);
        var visibleMarkdown = NormalizeLineEndings(visibleMap.VisibleText);

        var exactIndex = visibleMarkdown.IndexOf(visibleSelection, StringComparison.CurrentCultureIgnoreCase);
        if (exactIndex >= 0 && visibleMarkdown.IndexOf(visibleSelection, exactIndex + visibleSelection.Length, StringComparison.CurrentCultureIgnoreCase) < 0)
        {
            SetRangeFromVisibleMatch(visibleMap.SourceIndexes, exactIndex, visibleSelection.Length, out start, out length);
            ExpandRangeToMarkdownConstruct(ref start, ref length);
            return true;
        }

        var tokens = Regex.Split(visibleSelection, @"\s+")
            .Where(token => token.Length > 0)
            .Select(Regex.Escape)
            .ToArray();
        if (tokens.Length == 0)
        {
            return false;
        }

        var flexiblePattern = string.Join(@"\s+", tokens);
        var matches = Regex.Matches(visibleMarkdown, flexiblePattern, RegexOptions.IgnoreCase);
        if (matches.Count != 1)
        {
            return false;
        }

        SetRangeFromVisibleMatch(visibleMap.SourceIndexes, matches[0].Index, matches[0].Length, out start, out length);
        ExpandRangeToMarkdownConstruct(ref start, ref length);
        return true;
    }

    private bool TryFindMarkForSelectedText(string selectedText, out int start, out int length, out string innerMarkdown)
    {
        start = -1;
        length = 0;
        innerMarkdown = string.Empty;

        var selectedVisible = NormalizeForComparison(selectedText);
        var matches = Regex.Matches(currentMarkdown, @"<mark\b[^>]*>(?<inner>.*?)</mark>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        Match? partialMatch = null;

        foreach (Match match in matches)
        {
            var inner = match.Groups["inner"].Value;
            var innerVisible = NormalizeForComparison(BuildVisibleMarkdownMap(inner).VisibleText);
            if (string.Equals(selectedVisible, innerVisible, StringComparison.CurrentCultureIgnoreCase))
            {
                start = match.Index;
                length = match.Length;
                innerMarkdown = inner;
                return true;
            }

            if (innerVisible.Contains(selectedVisible, StringComparison.CurrentCultureIgnoreCase))
            {
                if (partialMatch is not null)
                {
                    partialMatch = null;
                    break;
                }

                partialMatch = match;
            }
        }

        if (partialMatch is not null)
        {
            start = partialMatch.Index;
            length = partialMatch.Length;
            innerMarkdown = partialMatch.Groups["inner"].Value;
            return true;
        }

        if (TryFindSelectedMarkdownRange(selectedText, out var selectedStart, out _)
            && TryFindContainingMark(selectedStart, out start, out length, out innerMarkdown))
        {
            return true;
        }

        return false;
    }

    private bool TryFindMarksIntersectingSelection(string selectedText, out List<(int Start, int Length, string InnerMarkdown)> marks)
    {
        marks = [];

        if (!TryFindSelectedMarkdownRange(selectedText, out var selectedStart, out var selectedLength))
        {
            return TryFindMarkForSelectedText(selectedText, out var markStart, out var markLength, out var markInner)
                && AddSingleMark(markStart, markLength, markInner, marks);
        }

        var selectedEnd = selectedStart + selectedLength;
        var matches = Regex.Matches(currentMarkdown, @"<mark\b[^>]*>(?<inner>.*?)</mark>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match match in matches)
        {
            var innerStart = match.Groups["inner"].Index;
            var innerEnd = innerStart + match.Groups["inner"].Length;
            if (selectedStart >= innerEnd || selectedEnd <= innerStart)
            {
                continue;
            }

            marks.Add((match.Index, match.Length, match.Groups["inner"].Value));
        }

        return marks.Count > 0;
    }

    private static bool AddSingleMark(int start, int length, string innerMarkdown, List<(int Start, int Length, string InnerMarkdown)> marks)
    {
        marks.Add((start, length, innerMarkdown));
        return true;
    }

    private bool TryFindContainingMark(int selectedStart, out int start, out int length, out string innerMarkdown)
    {
        start = -1;
        length = 0;
        innerMarkdown = string.Empty;

        var matches = Regex.Matches(currentMarkdown, @"<mark\b[^>]*>(?<inner>.*?)</mark>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match match in matches)
        {
            var innerStart = match.Groups["inner"].Index;
            var innerEnd = innerStart + match.Groups["inner"].Length;
            if (selectedStart < innerStart || selectedStart > innerEnd)
            {
                continue;
            }

            start = match.Index;
            length = match.Length;
            innerMarkdown = match.Groups["inner"].Value;
            return true;
        }

        return false;
    }

    private static VisibleMarkdownMap BuildVisibleMarkdownMap(string markdown)
    {
        var visible = new System.Text.StringBuilder();
        var sourceIndexes = new List<int>();
        var index = 0;
        var lineStart = true;

        while (index < markdown.Length)
        {
            if (StartsWith(markdown, index, "<mark"))
            {
                var close = markdown.IndexOf('>', index);
                if (close >= 0)
                {
                    index = close + 1;
                    continue;
                }
            }

            if (StartsWith(markdown, index, "</mark>"))
            {
                index += "</mark>".Length;
                continue;
            }

            if (lineStart && StartsWith(markdown, index, "```"))
            {
                index = MoveToNextLine(markdown, index);
                lineStart = true;
                continue;
            }

            if (lineStart)
            {
                while (index < markdown.Length && markdown[index] == '#')
                {
                    index++;
                }

                while (index < markdown.Length && markdown[index] == ' ')
                {
                    index++;
                }

                if (index < markdown.Length && markdown[index] == '>')
                {
                    index++;
                    if (index < markdown.Length && markdown[index] == ' ')
                    {
                        index++;
                    }
                }

                if (index + 1 < markdown.Length && (markdown[index] == '-' || markdown[index] == '*' || markdown[index] == '+') && markdown[index + 1] == ' ')
                {
                    index += 2;
                }
            }

            if (index >= markdown.Length)
            {
                break;
            }

            if (StartsWith(markdown, index, "!["))
            {
                var endImage = FindMarkdownLinkEnd(markdown, index + 1);
                if (endImage > index)
                {
                    index = endImage;
                    continue;
                }
            }

            if (markdown[index] == '[')
            {
                var closeLabel = markdown.IndexOf("](", index, StringComparison.Ordinal);
                var closeTarget = closeLabel >= 0 ? markdown.IndexOf(')', closeLabel + 2) : -1;
                if (closeLabel > index && closeTarget > closeLabel)
                {
                    for (var labelIndex = index + 1; labelIndex < closeLabel; labelIndex++)
                    {
                        visible.Append(markdown[labelIndex]);
                        sourceIndexes.Add(labelIndex);
                    }

                    index = closeTarget + 1;
                    lineStart = false;
                    continue;
                }
            }

            if (StartsWith(markdown, index, "**") || StartsWith(markdown, index, "__"))
            {
                index += 2;
                continue;
            }

            if (markdown[index] is '*' or '_' or '`')
            {
                index++;
                continue;
            }

            visible.Append(markdown[index]);
            sourceIndexes.Add(index);
            lineStart = markdown[index] == '\n';
            index++;
        }

        return new VisibleMarkdownMap(visible.ToString(), sourceIndexes);
    }

    private void ExpandRangeToMarkdownConstruct(ref int start, ref int length)
    {
        var end = start + length;

        var linkStart = currentMarkdown.LastIndexOf('[', start);
        var linkLabelEnd = linkStart >= 0 ? currentMarkdown.IndexOf("](", linkStart, StringComparison.Ordinal) : -1;
        var linkEnd = linkLabelEnd >= 0 ? currentMarkdown.IndexOf(')', linkLabelEnd + 2) : -1;
        if (linkStart >= 0 && linkLabelEnd >= end && linkEnd > linkLabelEnd)
        {
            start = linkStart;
            length = linkEnd - linkStart + 1;
            return;
        }

        ExpandAroundDelimiter(ref start, ref length, "**");
        ExpandAroundDelimiter(ref start, ref length, "__");
        ExpandAroundDelimiter(ref start, ref length, "`");
        ExpandAroundDelimiter(ref start, ref length, "*");
        ExpandAroundDelimiter(ref start, ref length, "_");
    }

    private void ExpandAroundDelimiter(ref int start, ref int length, string delimiter)
    {
        var end = start + length;
        if (start < delimiter.Length || end + delimiter.Length > currentMarkdown.Length)
        {
            return;
        }

        if (!string.Equals(currentMarkdown.Substring(start - delimiter.Length, delimiter.Length), delimiter, StringComparison.Ordinal)
            || !string.Equals(currentMarkdown.Substring(end, delimiter.Length), delimiter, StringComparison.Ordinal))
        {
            return;
        }

        start -= delimiter.Length;
        length += delimiter.Length * 2;
    }

    private static void SetRangeFromVisibleMatch(IReadOnlyList<int> sourceIndexes, int visibleStart, int visibleLength, out int start, out int length)
    {
        var visibleEnd = visibleStart + visibleLength - 1;
        start = sourceIndexes[visibleStart];
        var end = sourceIndexes[visibleEnd] + 1;
        length = end - start;
    }

    private static int FindMarkdownLinkEnd(string markdown, int openBracketIndex)
    {
        var closeLabel = markdown.IndexOf("](", openBracketIndex, StringComparison.Ordinal);
        return closeLabel >= 0 ? markdown.IndexOf(')', closeLabel + 2) + 1 : -1;
    }

    private static int MoveToNextLine(string text, int index)
    {
        var newline = text.IndexOf('\n', index);
        return newline >= 0 ? newline + 1 : text.Length;
    }

    private static bool StartsWith(string text, int index, string value)
    {
        return index + value.Length <= text.Length
            && string.Compare(text, index, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0;
    }

    private static string NormalizeForVisibleSearch(string text)
    {
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static string NormalizeForComparison(string text)
    {
        return Regex.Replace(NormalizeForVisibleSearch(text), @"\s+", " ");
    }

    private bool IsInsideExistingMark(int index)
    {
        var before = currentMarkdown[..index];
        var lastOpen = before.LastIndexOf("<mark", StringComparison.OrdinalIgnoreCase);
        var lastClose = before.LastIndexOf("</mark>", StringComparison.OrdinalIgnoreCase);
        return lastOpen > lastClose;
    }

    private void MarkDocumentChanged(string message)
    {
        hasUnsavedChanges = true;
        RenderCurrentDocument(preserveScroll: true);
        UpdateTitle();
        StatusText.Text = $"{message}. Pendiente de guardar.";
    }

    private bool Save()
    {
        if (currentFile is null)
        {
            SaveAs();
            return !hasUnsavedChanges;
        }

        try
        {
            DocumentFileService.Write(currentFile, currentMarkdown);
            hasUnsavedChanges = false;
            UpdateTitle();
            StatusText.Text = $"Guardado: {currentFile}";
            RememberRecentFile(currentFile);
            return true;
        }
        catch (Exception ex)
        {
            ShowFileOperationError("No se pudo guardar el archivo", currentFile, ex);
            return false;
        }
    }

    private void SaveAs()
    {
        var dialog = new SaveFileDialog
        {
            Filter = DocumentFileService.SaveFilter,
            FileName = currentFile is null ? "README.md" : Path.GetFileName(currentFile),
            InitialDirectory = currentFile is null
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : Path.GetDirectoryName(currentFile),
            Title = "Guardar Markdown como"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        currentFile = dialog.FileName;
        Save();
    }

    private void StartSearch(bool moveToFirstMatch)
    {
        var term = SearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(term))
        {
            activeSearchTerm = string.Empty;
            currentSearchIndex = -1;
            RenderCurrentDocument(preserveScroll: true);
            UpdateSearchUi();
            StatusText.Text = currentFile ?? "Listo para abrir archivos Markdown";
            return;
        }

        activeSearchTerm = term;
        currentSearchIndex = moveToFirstMatch ? 0 : currentSearchIndex;
        RenderCurrentDocument();

        if (searchMatches.Count == 0)
        {
            currentSearchIndex = -1;
            StatusText.Text = "Sin coincidencias";
            UpdateSearchUi();
            return;
        }

        currentSearchIndex = Math.Clamp(currentSearchIndex, 0, searchMatches.Count - 1);
        ApplySearchHighlights();
        BringCurrentMatchIntoView();
        UpdateSearchUi();
    }

    private void AdjustZoom(double delta)
    {
        SetZoom(readingZoom + delta);
    }

    private void SetZoom(double zoom)
    {
        readingZoom = Math.Max(Viewer.MinZoom, Math.Min(Viewer.MaxZoom, zoom));
        Viewer.Zoom = 100;
        SyncZoomScaleBox();
        userSettings.Zoom = readingZoom;
        userSettings.Save();

        if (Viewer.Document is not null)
        {
            RenderCurrentDocument(preserveScroll: true);
        }
    }

    private void SyncZoomScaleBox()
    {
        if (ZoomScaleBox is null)
        {
            return;
        }

        isUpdatingZoomScale = true;
        try
        {
            foreach (var item in ZoomScaleBox.Items.OfType<ComboBoxItem>())
            {
                if (double.TryParse(item.Tag?.ToString(), out var zoom) && Math.Abs(zoom - readingZoom) < 0.1)
                {
                    ZoomScaleBox.SelectedItem = item;
                    return;
                }
            }

            var customZoomItem = new ComboBoxItem
            {
                Content = $"{readingZoom:0}%",
                Tag = readingZoom
            };
            ZoomScaleBox.Items.Add(customZoomItem);
            ZoomScaleBox.SelectedItem = customZoomItem;
            ApplyCurrentComboBoxTheme(ZoomScaleBox);
        }
        finally
        {
            isUpdatingZoomScale = false;
        }
    }

    private void ToggleDarkMode()
    {
        isDarkMode = !isDarkMode;
        ThemeToggle.IsChecked = isDarkMode;
        RenderCurrentDocument(preserveScroll: true);
        ApplyChromeTheme();
        userSettings.IsDarkMode = isDarkMode;
        userSettings.Save();
    }

    private void RenderCurrentDocument(bool preserveScroll = false)
    {
        var scrollOffset = preserveScroll ? GetDocumentScrollOffset() : null;

        Viewer.Document = MarkdownDocumentRenderer.Render(
            currentMarkdown,
            currentFile is null ? null : Path.GetDirectoryName(currentFile),
            isDarkMode,
            GetDocumentViewportWidth(),
            isFullScreen,
            readingZoom);
        lastRenderViewportWidth = GetDocumentViewportWidth();
        Viewer.Opacity = 0.88;
        Viewer.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        ApplySearchHighlights();

        if (scrollOffset.HasValue)
        {
            RestoreDocumentScrollOffset(scrollOffset.Value);
        }
    }

    private void ApplyChromeTheme()
    {
        var windowBackground = Brush(isDarkMode ? "#111110" : "#E7EBF0");
        var toolbarBackground = Brush(isDarkMode ? "#181716" : "#F8FAFC");
        var toolbarBorder = Brush(isDarkMode ? "#2B2926" : "#CBD5E1");
        var documentBackground = Brush(isDarkMode ? "#1B1A18" : "#FFFFFF");
        var documentBorder = Brush(isDarkMode ? "#3A3732" : "#D5DCE5");
        var text = Brush(isDarkMode ? "#D8D2C7" : "#475569");
        var buttonBackground = Brush(isDarkMode ? "#24221F" : "#FFFFFF");
        var buttonBorder = Brush(isDarkMode ? "#48443D" : "#CBD5E1");
        var buttonText = Brush(isDarkMode ? "#ECE7DE" : "#1F2937");
        var mutedText = Brush(isDarkMode ? "#A8A29E" : "#64748B");
        var hoverBackground = Brush(isDarkMode ? "#2E2A25" : "#F1F5F9");
        var selectedBackground = Brush(isDarkMode ? "#4B3F20" : "#EAF2FF");
        var selectedText = Brush(isDarkMode ? "#FFF7E8" : "#0F172A");
        var disabledBackground = Brush(isDarkMode ? "#211F1C" : "#E2E8F0");

        Resources["ControlBackgroundBrush"] = buttonBackground;
        Resources["ControlBorderBrush"] = buttonBorder;
        Resources["ControlTextBrush"] = buttonText;
        Resources["MutedTextBrush"] = mutedText;
        Resources["ControlHoverBrush"] = hoverBackground;
        Resources["ControlSelectionBrush"] = selectedBackground;
        Resources["ControlSelectionTextBrush"] = selectedText;
        Resources["ControlDisabledBrush"] = disabledBackground;

        AnimateBackground(this, windowBackground.Color);
        AnimateBackground(Toolbar, toolbarBackground.Color);
        Toolbar.BorderBrush = toolbarBorder;
        AnimateBackground(DocumentFrame, documentBackground.Color);
        DocumentFrame.BorderBrush = documentBorder;
        AnimateBackground(Viewer, documentBackground.Color);
        AnimateBackground(SearchPanel, buttonBackground.Color);
        SearchPanel.BorderBrush = buttonBorder;
        FloatingMarkupToolbar.Background = toolbarBackground;
        FloatingMarkupToolbar.BorderBrush = toolbarBorder;
        StatusText.Foreground = text;
        SearchCountText.Foreground = text;
        ThemeToggle.IsChecked = isDarkMode;
        BrandLogo.Source = new BitmapImage(new Uri(
            isDarkMode ? "Assets/markit-logo-horizontal-dark.png" : "Assets/markit-logo-horizontal-light.png",
            UriKind.Relative));

        foreach (var separator in FindVisualChildren<Border>(Toolbar)
                     .Where(border => Math.Abs(border.Width - 1) < 0.1 && border.Height >= 20))
        {
            separator.Background = toolbarBorder;
        }

        foreach (var button in FindVisualChildren<Button>(this))
        {
            button.Background = buttonBackground;
            button.BorderBrush = buttonBorder;
            button.Foreground = buttonText;
        }

        SearchBox.Background = buttonBackground;
        SearchBox.BorderBrush = buttonBorder;
        SearchBox.Foreground = buttonText;
        SearchBox.CaretBrush = buttonText;
        SearchBox.Resources[SystemColors.HighlightBrushKey] = selectedBackground;
        SearchBox.Resources[SystemColors.HighlightTextBrushKey] = selectedText;

        ApplyComboBoxTheme(RecentFilesBox, buttonBackground, buttonBorder, buttonText, selectedBackground, selectedText, mutedText);
        ApplyComboBoxTheme(ZoomScaleBox, buttonBackground, buttonBorder, buttonText, selectedBackground, selectedText, mutedText);

        ApplyMarkupToolState();
        UpdateSearchUi();
    }

    private void ApplyCurrentComboBoxTheme(ComboBox comboBox)
    {
        ApplyComboBoxTheme(
            comboBox,
            Brush(isDarkMode ? "#24221F" : "#FFFFFF"),
            Brush(isDarkMode ? "#48443D" : "#CBD5E1"),
            Brush(isDarkMode ? "#ECE7DE" : "#1F2937"),
            Brush(isDarkMode ? "#4B3F20" : "#EAF2FF"),
            Brush(isDarkMode ? "#FFF7E8" : "#0F172A"),
            Brush(isDarkMode ? "#A8A29E" : "#64748B"));
    }

    private void ApplyComboBoxTheme(
        ComboBox comboBox,
        Brush background,
        Brush border,
        Brush text,
        Brush selectedBackground,
        Brush selectedText,
        Brush mutedText)
    {
        comboBox.Background = background;
        comboBox.BorderBrush = border;
        comboBox.Foreground = text;

        comboBox.Resources[SystemColors.WindowBrushKey] = background;
        comboBox.Resources[SystemColors.ControlBrushKey] = background;
        comboBox.Resources[SystemColors.ControlTextBrushKey] = text;
        comboBox.Resources[SystemColors.GrayTextBrushKey] = mutedText;
        comboBox.Resources[SystemColors.HighlightBrushKey] = selectedBackground;
        comboBox.Resources[SystemColors.HighlightTextBrushKey] = selectedText;

        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            item.ClearValue(Control.BackgroundProperty);
            item.ClearValue(Control.BorderBrushProperty);
            item.ClearValue(Control.ForegroundProperty);
        }

        foreach (var innerTextBox in FindVisualChildren<TextBox>(comboBox))
        {
            innerTextBox.Background = background;
            innerTextBox.BorderBrush = border;
            innerTextBox.Foreground = text;
            innerTextBox.CaretBrush = text;
            innerTextBox.Resources[SystemColors.HighlightBrushKey] = selectedBackground;
            innerTextBox.Resources[SystemColors.HighlightTextBrushKey] = selectedText;
        }
    }

    private void ApplySearchHighlights()
    {
        searchMatches.Clear();
        if (Viewer.Document is null || string.IsNullOrWhiteSpace(activeSearchTerm))
        {
            return;
        }

        searchMatches.AddRange(FindMatches(Viewer.Document, activeSearchTerm));
        if (searchMatches.Count == 0)
        {
            return;
        }

        currentSearchIndex = Math.Clamp(currentSearchIndex, 0, searchMatches.Count - 1);
        var matchBrush = Brush(isDarkMode ? "#4B3F20" : "#FEF3C7");
        var currentBrush = Brush(isDarkMode ? "#B8892F" : "#F59E0B");
        var currentTextBrush = Brush(isDarkMode ? "#111110" : "#111827");

        for (var i = 0; i < searchMatches.Count; i++)
        {
            searchMatches[i].ApplyPropertyValue(TextElement.BackgroundProperty, i == currentSearchIndex ? currentBrush : matchBrush);
            if (i == currentSearchIndex)
            {
                searchMatches[i].ApplyPropertyValue(TextElement.ForegroundProperty, currentTextBrush);
            }
        }
    }

    private void HandleEscape()
    {
        if (SearchPanel.Visibility == Visibility.Visible && (SearchBox.IsKeyboardFocusWithin || !string.IsNullOrWhiteSpace(activeSearchTerm)))
        {
            HideSearchPanel(clearSearch: true);
            StatusText.Text = currentFile ?? "Listo para abrir archivos Markdown";
            return;
        }

        if (activeMarkupTool != MarkupTool.None)
        {
            ClearMarkupTool();
            StatusText.Text = currentFile ?? "Listo para abrir archivos Markdown";
            return;
        }

        ExitFullScreen();
    }

    private void GoToNextMatch()
    {
        if (searchMatches.Count == 0)
        {
            StartSearch(moveToFirstMatch: true);
            return;
        }

        currentSearchIndex = (currentSearchIndex + 1) % searchMatches.Count;
        RenderCurrentDocument();
        BringCurrentMatchIntoView();
        UpdateSearchUi();
    }

    private void GoToPreviousMatch()
    {
        if (searchMatches.Count == 0)
        {
            StartSearch(moveToFirstMatch: true);
            return;
        }

        currentSearchIndex = currentSearchIndex <= 0 ? searchMatches.Count - 1 : currentSearchIndex - 1;
        RenderCurrentDocument();
        BringCurrentMatchIntoView();
        UpdateSearchUi();
    }

    private void BringCurrentMatchIntoView()
    {
        if (currentSearchIndex < 0 || currentSearchIndex >= searchMatches.Count)
        {
            return;
        }

        searchMatches[currentSearchIndex].Start.Paragraph?.BringIntoView();
    }

    private double? GetDocumentScrollOffset()
    {
        return GetDocumentScrollViewer()?.VerticalOffset;
    }

    private void RestoreDocumentScrollOffset(double offset)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            GetDocumentScrollViewer()?.ScrollToVerticalOffset(offset);
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private double GetDocumentViewportWidth()
    {
        if (DocumentFrame.ActualWidth > 0)
        {
            return DocumentFrame.ActualWidth;
        }

        return ActualWidth > 0 ? ActualWidth : Width;
    }

    private ScrollViewer? GetDocumentScrollViewer()
    {
        return FindVisualChildren<ScrollViewer>(Viewer)
            .FirstOrDefault(scroll => scroll.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled)
            ?? FindVisualChildren<ScrollViewer>(Viewer).FirstOrDefault();
    }

    private void SmoothScrollBy(double delta)
    {
        var scrollViewer = GetDocumentScrollViewer();
        if (scrollViewer is null)
        {
            return;
        }

        smoothScrollTarget = Math.Clamp(scrollViewer.VerticalOffset + delta, 0, scrollViewer.ScrollableHeight);
        if (isSmoothScrolling)
        {
            return;
        }

        isSmoothScrolling = true;
        CompositionTarget.Rendering += SmoothScroll_Rendering;
    }

    private void SmoothScroll_Rendering(object? sender, EventArgs e)
    {
        var scrollViewer = GetDocumentScrollViewer();
        if (scrollViewer is null)
        {
            StopSmoothScroll();
            return;
        }

        var current = scrollViewer.VerticalOffset;
        var distance = smoothScrollTarget - current;
        if (Math.Abs(distance) < 0.5)
        {
            scrollViewer.ScrollToVerticalOffset(smoothScrollTarget);
            StopSmoothScroll();
            return;
        }

        scrollViewer.ScrollToVerticalOffset(current + distance * 0.24);
    }

    private void StopSmoothScroll()
    {
        CompositionTarget.Rendering -= SmoothScroll_Rendering;
        isSmoothScrolling = false;
    }

    private void FocusSearch()
    {
        ShowSearchPanel(focusSearch: false);
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void ShowSearchPanel(bool focusSearch)
    {
        SearchPanel.Visibility = Visibility.Visible;
        UpdateSearchUi();

        if (focusSearch)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }
    }

    private void HideSearchPanel(bool clearSearch)
    {
        if (clearSearch)
        {
            SearchBox.Text = string.Empty;
            activeSearchTerm = string.Empty;
            currentSearchIndex = -1;
            RenderCurrentDocument(preserveScroll: true);
        }

        SearchPanel.Visibility = Visibility.Collapsed;
        UpdateSearchUi();
    }

    private void RememberRecentFile(string fileName)
    {
        userSettings.AddRecentFile(fileName);
        userSettings.Save();
        RefreshRecentFiles();
    }

    private void RefreshRecentFiles()
    {
        isUpdatingRecentFiles = true;
        RecentFilesBox.Items.Clear();

        var existingFiles = userSettings.RecentFiles.Where(File.Exists).ToList();
        if (existingFiles.Count != userSettings.RecentFiles.Count)
        {
            userSettings.RecentFiles = existingFiles;
            userSettings.Save();
        }

        RecentFilesBox.Items.Add(new ComboBoxItem
        {
            Content = existingFiles.Count == 0 ? "Sin recientes" : "Recientes",
            IsEnabled = false
        });

        foreach (var file in existingFiles)
        {
            RecentFilesBox.Items.Add(new ComboBoxItem
            {
                Content = Path.GetFileName(file),
                ToolTip = file,
                Tag = file
            });
        }

        RecentFilesBox.SelectedIndex = 0;
        isUpdatingRecentFiles = false;
    }

    private void SaveCurrentSettings()
    {
        userSettings.IsDarkMode = isDarkMode;
        userSettings.Zoom = readingZoom;

        if (!isFullScreen && WindowState == WindowState.Normal)
        {
            userSettings.WindowWidth = ActualWidth;
            userSettings.WindowHeight = ActualHeight;
        }

        userSettings.Save();
    }

    private void UpdateSearchUi()
    {
        var hasMatches = searchMatches.Count > 0;
        var isSearching = SearchPanel.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(SearchBox.Text);
        PreviousMatchButton.IsEnabled = hasMatches;
        NextMatchButton.IsEnabled = hasMatches;
        PreviousMatchButton.Visibility = hasMatches ? Visibility.Visible : Visibility.Collapsed;
        NextMatchButton.Visibility = hasMatches ? Visibility.Visible : Visibility.Collapsed;
        SearchCountText.Text = hasMatches ? $"{currentSearchIndex + 1}/{searchMatches.Count}" : isSearching ? "0/0" : string.Empty;
    }

    private static IEnumerable<TextRange> FindMatches(FlowDocument document, string term)
    {
        var pointer = document.ContentStart;
        while (pointer is not null && pointer.CompareTo(document.ContentEnd) < 0)
        {
            if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                var text = pointer.GetTextInRun(LogicalDirection.Forward);
                var index = 0;
                while ((index = text.IndexOf(term, index, StringComparison.CurrentCultureIgnoreCase)) >= 0)
                {
                    var start = pointer.GetPositionAtOffset(index);
                    var end = pointer.GetPositionAtOffset(index + term.Length);
                    if (start is not null && end is not null)
                    {
                        yield return new TextRange(start, end);
                    }

                    index += term.Length;
                }

                pointer = pointer.GetPositionAtOffset(text.Length);
            }
            else
            {
                pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
            }
        }
    }

    private void ToggleFullScreen()
    {
        if (isFullScreen)
        {
            ExitFullScreen();
            return;
        }

        previousWindowStyle = WindowStyle;
        previousWindowState = WindowState;
        previousResizeMode = ResizeMode;
        previousDocumentMargin = DocumentFrame.Margin;

        isFullScreen = true;
        Toolbar.Visibility = Visibility.Collapsed;
        DocumentFrame.Margin = new Thickness(0);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        FullScreenButton.ToolTip = "Salir de pantalla completa";
        Dispatcher.BeginInvoke(new Action(() => RenderCurrentDocument(preserveScroll: true)));
    }

    private void ExitFullScreen()
    {
        if (!isFullScreen)
        {
            return;
        }

        isFullScreen = false;
        Toolbar.Visibility = Visibility.Visible;
        DocumentFrame.Margin = previousDocumentMargin;
        WindowStyle = previousWindowStyle;
        ResizeMode = previousResizeMode;
        WindowState = previousWindowState;
        FloatingMarkupToolbar.Visibility = Visibility.Collapsed;
        FullScreenButton.ToolTip = "Pantalla completa";
        Dispatcher.BeginInvoke(new Action(() => RenderCurrentDocument(preserveScroll: true)));
    }

    private bool ConfirmDiscardUnsavedChanges()
    {
        if (!hasUnsavedChanges)
        {
            return true;
        }

        var result = MessageBox.Show(
            "Hay cambios sin guardar.\n\nSi elegis Si, se guardan los cambios.\nSi elegis No, Markit sale sin guardar.",
            "Cambios sin guardar",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        return result switch
        {
            MessageBoxResult.Yes => Save(),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private void ShowFileOperationError(string title, string fileName, Exception exception)
    {
        var reason = DocumentFileService.DescribeFailure(exception);

        MessageBox.Show(
            $"{reason}\n\nArchivo:\n{fileName}\n\nDetalle:\n{exception.Message}",
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        StatusText.Text = title;
    }

    private void UpdateTitle()
    {
        var name = currentFile is null ? "markit" : $"{Path.GetFileName(currentFile)} - markit";
        Title = hasUnsavedChanges ? $"{name} *" : name;
    }

    private static string NormalizeSelectedText(string text)
    {
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static System.Windows.Media.SolidColorBrush Brush(string color)
    {
        var brush = (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }

    private static void AnimateBackground(Control control, Color toColor) => AnimateBackgroundBrush(control, Control.BackgroundProperty, toColor);

    private static void AnimateBackground(Border border, Color toColor) => AnimateBackgroundBrush(border, Border.BackgroundProperty, toColor);

    private static void AnimateBackground(Window window, Color toColor) => AnimateBackgroundBrush(window, Window.BackgroundProperty, toColor);

    private static void AnimateBackgroundBrush(DependencyObject target, DependencyProperty property, Color toColor)
    {
        var currentBrush = target.GetValue(property) as SolidColorBrush;
        var fromColor = currentBrush?.Color ?? toColor;
        var animatedBrush = new SolidColorBrush(fromColor);
        target.SetValue(property, animatedBrush);
        animatedBrush.BeginAnimation(
            SolidColorBrush.ColorProperty,
            new ColorAnimation(toColor, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private static bool HasSupportedDocumentDrop(DragEventArgs e)
    {
        return e.Data.GetDataPresent(DataFormats.FileDrop)
            && DocumentFileService.FirstSupportedDocument((string[])e.Data.GetData(DataFormats.FileDrop)!) is not null;
    }

    private sealed record VisibleMarkdownMap(string VisibleText, IReadOnlyList<int> SourceIndexes);

    private enum MarkupTool
    {
        None,
        Highlight,
        Erase
    }
}

internal sealed class RelayCommand : ICommand
{
    private readonly Action<object?> execute;

    public RelayCommand(Action<object?> execute)
    {
        this.execute = execute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute(parameter);
}
