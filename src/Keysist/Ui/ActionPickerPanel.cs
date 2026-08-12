using System.Windows.Forms;
using Keysist.Capture;
using Keysist.Config;
using Keysist.Pipeline;

namespace Keysist.Ui;

public sealed class ActionPickerPanel : UserControl
{
    private readonly RadioButton _sendKeysRadio = new() { Text = "Send keys", AutoSize = true };
    private readonly RadioButton _pasteRadio = new() { Text = "Paste text", AutoSize = true };
    private readonly RadioButton _launchRadio = new() { Text = "Launch", AutoSize = true };

    private readonly Panel _sendKeysPanel = new() { Dock = DockStyle.Fill, Visible = true };
    private readonly Panel _pastePanel = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Panel _launchPanel = new() { Dock = DockStyle.Fill, Visible = false };

    private readonly CaptureControl _actionCapture;
    private readonly TextBox _pasteTextBox = new() { Multiline = true, Width = 360, Height = 80, ScrollBars = ScrollBars.Vertical };
    private readonly ComboBox _pasteMethodCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly TextBox _pathBox = new() { Width = 360 };
    private readonly TextBox _argsBox = new() { Width = 360 };
    private readonly TextBox _workDirBox = new() { Width = 360 };

    public ActionPickerPanel(InputPipeline pipeline)
    {
        _actionCapture = new CaptureControl(pipeline) { Dock = DockStyle.Top };

        var typeRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 8),
        };
        typeRow.Controls.Add(_sendKeysRadio);
        typeRow.Controls.Add(_pasteRadio);
        typeRow.Controls.Add(_launchRadio);
        _sendKeysRadio.Checked = true;

        BuildSendKeysPanel();
        BuildPastePanel();
        BuildLaunchPanel();

        var content = new Panel { Dock = DockStyle.Fill };
        content.Controls.Add(_launchPanel);
        content.Controls.Add(_pastePanel);
        content.Controls.Add(_sendKeysPanel);

        Controls.Add(content);
        Controls.Add(typeRow);

        _sendKeysRadio.CheckedChanged += (_, _) => UpdatePanels();
        _pasteRadio.CheckedChanged += (_, _) => UpdatePanels();
        _launchRadio.CheckedChanged += (_, _) => UpdatePanels();
        UpdatePanels();
    }

    private void BuildSendKeysPanel()
    {
        _sendKeysPanel.Controls.Add(_actionCapture);
    }

    private void BuildPastePanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(0, 4, 0, 0),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _pasteMethodCombo.Items.AddRange(["type", "clipboard"]);
        _pasteMethodCombo.SelectedIndex = 0;

        layout.Controls.Add(new Label { Text = "Text:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(_pasteTextBox, 1, 0);
        layout.Controls.Add(new Label { Text = "Method:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(_pasteMethodCombo, 1, 1);

        _pastePanel.Controls.Add(layout);
    }

    private void BuildLaunchPanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(0, 4, 0, 0),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        layout.Controls.Add(new Label { Text = "Path:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(_pathBox, 1, 0);
        layout.Controls.Add(new Label { Text = "Arguments:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(_argsBox, 1, 1);
        layout.Controls.Add(new Label { Text = "Working directory:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        layout.Controls.Add(_workDirBox, 1, 2);

        _launchPanel.Controls.Add(layout);
    }

    private void UpdatePanels()
    {
        _sendKeysPanel.Visible = _sendKeysRadio.Checked;
        _pastePanel.Visible = _pasteRadio.Checked;
        _launchPanel.Visible = _launchRadio.Checked;
    }

    public bool TryBuildAction(out ActionConfig action, IWin32Window? owner = null)
    {
        action = new ActionConfig();

        if (_sendKeysRadio.Checked)
        {
            if (_actionCapture.Result is not { Keys.Count: > 0 } result)
            {
                MessageBox.Show(owner, "Capture at least one key for the action.", "Send keys");
                return false;
            }

            action.Type = "sendKeys";
            action.Steps = CaptureToSendSteps(result);
            return true;
        }

        if (_pasteRadio.Checked)
        {
            if (string.IsNullOrWhiteSpace(_pasteTextBox.Text))
            {
                MessageBox.Show(owner, "Enter text to paste.", "Paste text");
                return false;
            }

            action.Type = "pasteText";
            action.Text = _pasteTextBox.Text;
            action.Method = _pasteMethodCombo.SelectedItem as string ?? "type";
            return true;
        }

        if (string.IsNullOrWhiteSpace(_pathBox.Text))
        {
            MessageBox.Show(owner, "Enter a path to launch.", "Launch");
            return false;
        }

        action.Type = "launch";
        action.Path = _pathBox.Text.Trim();
        action.Args = string.IsNullOrWhiteSpace(_argsBox.Text) ? null : _argsBox.Text;
        action.WorkingDirectory = string.IsNullOrWhiteSpace(_workDirBox.Text) ? null : _workDirBox.Text;
        return true;
    }

    internal static List<SendStep> CaptureToSendSteps(CaptureResult result)
    {
        if (result.Keys.Count == 1)
        {
            return [new SendStep { Send = "vkey", Value = result.Keys[0] }];
        }

        if (result.Mode == CaptureMode.Sequence)
        {
            return result.Keys
                .Select(key => new SendStep { Send = "vkey", Value = key, DelayMs = 0 })
                .ToList();
        }

        var steps = new List<SendStep>();
        for (var i = 0; i < result.Keys.Count - 1; i++)
            steps.Add(new SendStep { Send = "keydown", Value = result.Keys[i] });

        steps.Add(new SendStep { Send = "vkey", Value = result.Keys[^1] });

        for (var i = result.Keys.Count - 2; i >= 0; i--)
            steps.Add(new SendStep { Send = "keyup", Value = result.Keys[i] });

        return steps;
    }
}
