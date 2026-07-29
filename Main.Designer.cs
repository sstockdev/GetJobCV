namespace GetJobCV
{
    partial class Main
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            SelectButton = new Button();
            StatusLabel = new Label();
            DebugTextBox = new RichTextBox();
            SuspendLayout();
            // 
            // SelectButton
            // 
            SelectButton.Location = new Point(242, 162);
            SelectButton.Name = "SelectButton";
            SelectButton.Size = new Size(292, 88);
            SelectButton.TabIndex = 0;
            SelectButton.Text = "Select PDF";
            SelectButton.UseVisualStyleBackColor = true;
            SelectButton.Click += SelectButton_Click;
            // 
            // StatusLabel
            // 
            StatusLabel.AutoSize = true;
            StatusLabel.Location = new Point(367, 269);
            StatusLabel.Name = "StatusLabel";
            StatusLabel.Size = new Size(39, 15);
            StatusLabel.TabIndex = 1;
            StatusLabel.Text = "Ready";
            // 
            // DebugTextBox
            // 
            DebugTextBox.Location = new Point(242, 12);
            DebugTextBox.Name = "DebugTextBox";
            DebugTextBox.Size = new Size(292, 144);
            DebugTextBox.TabIndex = 2;
            DebugTextBox.Text = "";
            // 
            // Main
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(DebugTextBox);
            Controls.Add(StatusLabel);
            Controls.Add(SelectButton);
            Name = "Main";
            Text = "GetJobCV - Main";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button SelectButton;
        private Label StatusLabel;
        private RichTextBox DebugTextBox;
    }
}
