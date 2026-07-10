using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using TrystybellKoreanPatcher.Patching;

namespace TrystybellKoreanPatcher
{
    public sealed class MainForm : Form
    {
        private readonly Dictionary<PatchStage, Label> stageStatusLabels;

        private Panel dropPanel;
        private Label dropHintLabel;
        private Button patchButton;
        private ProgressBar progressBar;
        private TextBox logTextBox;

        private string selectedIsoPath;
        private BackgroundWorker patchWorker;

        public MainForm()
        {
            stageStatusLabels = new Dictionary<PatchStage, Label>();

            Text = "트러스티 벨 한글 패처";
            Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(246, 247, 250);
            MinimumSize = new Size(820, 470);
            Size = new Size(960, 540);
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;

            BuildLayout();
            ResetStageStatuses();

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
        }

        private void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.ColumnCount = 3;
            root.RowCount = 3;
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(18);
            root.BackColor = BackColor;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118F));
            Controls.Add(root);

            dropPanel = BuildDropPanel();
            root.Controls.Add(dropPanel, 0, 0);

            Panel stagePanel = BuildStagePanel();
            root.Controls.Add(stagePanel, 1, 0);

            Panel actionPanel = BuildActionPanel();
            root.Controls.Add(actionPanel, 2, 0);

            progressBar = new ProgressBar();
            progressBar.Dock = DockStyle.Fill;
            progressBar.Minimum = 0;
            progressBar.Maximum = 100;
            progressBar.Value = 0;
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Margin = new Padding(0, 10, 0, 4);
            root.Controls.Add(progressBar, 0, 1);
            root.SetColumnSpan(progressBar, 3);

            logTextBox = new TextBox();
            logTextBox.Dock = DockStyle.Fill;
            logTextBox.Multiline = true;
            logTextBox.ReadOnly = true;
            logTextBox.ScrollBars = ScrollBars.Vertical;
            logTextBox.BorderStyle = BorderStyle.FixedSingle;
            logTextBox.BackColor = Color.White;
            logTextBox.ForeColor = Color.FromArgb(55, 63, 76);
            logTextBox.Text = "대기 중";
            root.Controls.Add(logTextBox, 0, 2);
            root.SetColumnSpan(logTextBox, 3);
        }

        private Panel BuildDropPanel()
        {
            Panel panel = new Panel();
            panel.AllowDrop = true;
            panel.BackColor = Color.White;
            panel.BorderStyle = BorderStyle.FixedSingle;
            panel.Dock = DockStyle.Fill;
            panel.Margin = new Padding(0, 0, 14, 0);
            panel.Cursor = Cursors.Hand;

            TableLayoutPanel inner = new TableLayoutPanel();
            inner.ColumnCount = 1;
            inner.RowCount = 3;
            inner.Dock = DockStyle.Fill;
            inner.Padding = new Padding(22);
            inner.BackColor = Color.White;
            inner.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 104F));
            inner.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            panel.Controls.Add(inner);

            TableLayoutPanel center = new TableLayoutPanel();
            center.ColumnCount = 1;
            center.RowCount = 2;
            center.Dock = DockStyle.Fill;
            center.BackColor = Color.White;
            center.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
            center.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            inner.Controls.Add(center, 0, 1);

            Label titleLabel = new Label();
            titleLabel.Dock = DockStyle.Fill;
            titleLabel.TextAlign = ContentAlignment.MiddleCenter;
            titleLabel.Font = new Font(Font.FontFamily, 13F, FontStyle.Bold, GraphicsUnit.Point);
            titleLabel.ForeColor = Color.FromArgb(34, 42, 54);
            titleLabel.UseMnemonic = false;
            titleLabel.Text = "일본판 Trusty Bell ISO를\r\n여기에 드래그 & 드롭";
            center.Controls.Add(titleLabel, 0, 0);

            dropHintLabel = new Label();
            dropHintLabel.Dock = DockStyle.Fill;
            dropHintLabel.TextAlign = ContentAlignment.TopCenter;
            dropHintLabel.AutoEllipsis = true;
            dropHintLabel.UseMnemonic = false;
            dropHintLabel.ForeColor = Color.FromArgb(92, 101, 116);
            dropHintLabel.Text = "클릭해서 ISO 파일 선택";
            center.Controls.Add(dropHintLabel, 0, 1);

            panel.Click += OnDropPanelClick;
            inner.Click += OnDropPanelClick;
            center.Click += OnDropPanelClick;
            titleLabel.Click += OnDropPanelClick;
            dropHintLabel.Click += OnDropPanelClick;
            panel.DragEnter += OnDragEnter;
            panel.DragDrop += OnDragDrop;
            inner.DragEnter += OnDragEnter;
            inner.DragDrop += OnDragDrop;
            center.DragEnter += OnDragEnter;
            center.DragDrop += OnDragDrop;

            return panel;
        }

        private Panel BuildStagePanel()
        {
            Panel panel = new Panel();
            panel.BackColor = BackColor;
            panel.Dock = DockStyle.Fill;
            panel.Margin = new Padding(4, 0, 14, 0);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.ColumnCount = 2;
            layout.RowCount = PatchStageCatalog.GetStages().Length + 2;
            layout.Dock = DockStyle.Fill;
            layout.BackColor = BackColor;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            panel.Controls.Add(layout);

            Label title = new Label();
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold, GraphicsUnit.Point);
            title.ForeColor = Color.FromArgb(34, 42, 54);
            title.Text = "진행 상태";
            layout.Controls.Add(title, 0, 0);
            layout.SetColumnSpan(title, 2);

            PatchStage[] stages = PatchStageCatalog.GetStages();
            for (int i = 0; i < stages.Length; i++)
            {
                PatchStage stage = stages[i];
                int row = i + 1;
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

                Label nameLabel = new Label();
                nameLabel.Dock = DockStyle.Fill;
                nameLabel.TextAlign = ContentAlignment.MiddleLeft;
                nameLabel.ForeColor = Color.FromArgb(65, 72, 86);
                nameLabel.Text = PatchStageCatalog.GetDisplayName(stage);
                layout.Controls.Add(nameLabel, 0, row);

                Label statusLabel = new Label();
                statusLabel.Dock = DockStyle.Fill;
                statusLabel.TextAlign = ContentAlignment.MiddleRight;
                statusLabel.ForeColor = Color.FromArgb(116, 126, 142);
                layout.Controls.Add(statusLabel, 1, row);
                stageStatusLabels[stage] = statusLabel;
            }

            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            return panel;
        }

        private Panel BuildActionPanel()
        {
            Panel panel = new Panel();
            panel.BackColor = BackColor;
            panel.Dock = DockStyle.Fill;
            panel.Margin = new Padding(4, 0, 0, 0);

            patchButton = new Button();
            patchButton.Anchor = AnchorStyles.None;
            patchButton.Size = new Size(142, 58);
            patchButton.Text = "패치 시작";
            patchButton.Font = new Font(Font.FontFamily, 12F, FontStyle.Bold, GraphicsUnit.Point);
            patchButton.Enabled = false;
            patchButton.Click += OnPatchButtonClick;
            panel.Controls.Add(patchButton);
            panel.Resize += delegate
            {
                patchButton.Left = Math.Max(0, (panel.ClientSize.Width - patchButton.Width) / 2);
                patchButton.Top = Math.Max(0, (panel.ClientSize.Height - patchButton.Height) / 2);
            };

            return panel;
        }

        private void OnDropPanelClick(object sender, EventArgs e)
        {
            if (IsBusy())
            {
                return;
            }

            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "원본 ISO 파일 선택";
                dialog.Filter = "ISO files (*.iso)|*.iso|All files (*.*)|*.*";
                dialog.Multiselect = false;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    SelectIso(dialog.FileName);
                }
            }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            string path = TryGetDroppedIso(e);
            e.Effect = path == null ? DragDropEffects.None : DragDropEffects.Copy;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            string path = TryGetDroppedIso(e);
            if (path == null)
            {
                AppendLog("선택한 파일은 ISO가 아닙니다.");
                return;
            }

            SelectIso(path);
        }

        private void SelectIso(string path)
        {
            if (!IsIsoPath(path))
            {
                AppendLog("ISO 파일만 선택할 수 있습니다.");
                return;
            }

            selectedIsoPath = Path.GetFullPath(path);
            patchButton.Enabled = true;
            progressBar.Value = 0;
            ResetStageStatuses();
            dropHintLabel.Text = Path.GetFileName(path);
            logTextBox.Text = "ISO 선택됨: " + selectedIsoPath;
        }

        private void OnPatchButtonClick(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(selectedIsoPath))
            {
                AppendLog("패치할 ISO를 먼저 선택해 주세요.");
                return;
            }

            string outputPath = PatchStageCatalog.BuildDefaultOutputIsoPath(selectedIsoPath);
            if (File.Exists(outputPath))
            {
                DialogResult overwrite = MessageBox.Show(
                    this,
                    Path.GetFileName(outputPath) + " 파일이 이미 있습니다. 덮어쓸까요?",
                    "기존 패치 ISO 확인",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (overwrite != DialogResult.Yes)
                {
                    return;
                }
            }

            SetBusy(true);
            ResetStageStatuses();
            progressBar.Value = 0;
            AppendLog("한글 패치를 시작합니다.");

            PatchJobOptions options = new PatchJobOptions();
            options.InputIsoPath = selectedIsoPath;

            patchWorker = new BackgroundWorker();
            patchWorker.WorkerReportsProgress = true;
            patchWorker.DoWork += delegate(object workerSender, DoWorkEventArgs args)
            {
                BackgroundWorker worker = (BackgroundWorker)workerSender;
                TrustyBellPatchPipeline pipeline = new TrustyBellPatchPipeline();
                args.Result = pipeline.Run(
                    options,
                    delegate(PatchProgressInfo info)
                    {
                        worker.ReportProgress(info.Percent, info);
                    });
            };
            patchWorker.ProgressChanged += delegate(object workerSender, ProgressChangedEventArgs args)
            {
                PatchProgressInfo info = args.UserState as PatchProgressInfo;
                if (info != null)
                {
                    progressBar.Value = Math.Max(progressBar.Minimum, Math.Min(progressBar.Maximum, info.Percent));
                    UpdateStage(info.Stage, info.State);
                    AppendLog(info.Message);
                }
            };
            patchWorker.RunWorkerCompleted += delegate(object workerSender, RunWorkerCompletedEventArgs args)
            {
                SetBusy(false);
                if (args.Error != null)
                {
                    AppendLog("오류: " + args.Error.Message);
                    MessageBox.Show(this, args.Error.Message, "패치 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                PatchResult result = args.Result as PatchResult;
                if (result != null && result.Completed)
                {
                    progressBar.Value = 100;
                    AppendLog("완료: " + result.OutputIsoPath);
                    MessageBox.Show(
                        this,
                        "한글 패치가 완료되었습니다.\r\n\r\n" + result.OutputIsoPath,
                        "패치 완료",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                AppendLog("알 수 없는 상태로 종료되었습니다.");
            };
            patchWorker.RunWorkerAsync();
        }

        private void ResetStageStatuses()
        {
            foreach (KeyValuePair<PatchStage, Label> item in stageStatusLabels)
            {
                item.Value.Text = "대기";
                item.Value.ForeColor = Color.FromArgb(116, 126, 142);
            }
        }

        private void UpdateStage(PatchStage stage, PatchStageState state)
        {
            Label label;
            if (!stageStatusLabels.TryGetValue(stage, out label))
            {
                return;
            }

            switch (state)
            {
                case PatchStageState.Running:
                    label.Text = "진행";
                    label.ForeColor = Color.FromArgb(35, 101, 184);
                    break;
                case PatchStageState.Complete:
                    label.Text = "완료";
                    label.ForeColor = Color.FromArgb(28, 132, 86);
                    break;
                case PatchStageState.Blocked:
                    label.Text = "오류";
                    label.ForeColor = Color.FromArgb(190, 60, 52);
                    break;
                default:
                    label.Text = "대기";
                    label.ForeColor = Color.FromArgb(116, 126, 142);
                    break;
            }
        }

        private void SetBusy(bool busy)
        {
            dropPanel.Enabled = !busy;
            patchButton.Enabled = !busy && !String.IsNullOrEmpty(selectedIsoPath);
        }

        private bool IsBusy()
        {
            return patchWorker != null && patchWorker.IsBusy;
        }

        private void AppendLog(string message)
        {
            if (String.IsNullOrEmpty(logTextBox.Text) || logTextBox.Text == "대기 중")
            {
                logTextBox.Text = message;
            }
            else
            {
                logTextBox.AppendText(Environment.NewLine + message);
            }

            logTextBox.SelectionStart = logTextBox.TextLength;
            logTextBox.ScrollToCaret();
        }

        private static string TryGetDroppedIso(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return null;
            }

            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length != 1)
            {
                return null;
            }

            string path = files[0];
            return IsIsoPath(path) ? path : null;
        }

        private static bool IsIsoPath(string path)
        {
            return !String.IsNullOrEmpty(path)
                && File.Exists(path)
                && String.Equals(Path.GetExtension(path), ".iso", StringComparison.OrdinalIgnoreCase);
        }
    }
}
