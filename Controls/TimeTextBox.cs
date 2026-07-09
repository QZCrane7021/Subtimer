using Avalonia.Controls;
using Avalonia.Input;
using System;

namespace Subtimer.Controls
{
    public class SrtTimeTextBox : TextBox
    {
        public SrtTimeTextBox()
        {
            // .srt 格式 "00:00:00,000" 的长度固定为 12 位
            MaxLength = 12; 
        }

        // 复用 TextBox 的默认样式
        protected override Type StyleKeyOverride => typeof(TextBox);

        /// <summary>
        /// 初始化时赋 .srt 标准默认模板
        /// </summary>
        protected override void OnInitialized()
        {
            base.OnInitialized();
            if (string.IsNullOrEmpty(Text))
            {
                Text = "00:00:00,000"; 
            }
        }

        /// <summary>
        /// 处理数字输入（原地覆盖与跳过冒号、逗号）
        /// </summary>
        protected override void OnTextInput(TextInputEventArgs e)
        {
            // 如果有选中文本，走原生替换逻辑
            if (SelectionStart != SelectionEnd)
            {
                base.OnTextInput(e);
                return;
            }

            // 只允许数字输入
            if (string.IsNullOrEmpty(e.Text) || e.Text.Length != 1 || !char.IsDigit(e.Text[0]))
            {
                e.Handled = true; 
                return;
            }

            char inputChar = e.Text[0];
            string currentText = Text ?? "00:00:00,000";
            int caret = CaretIndex;

            // 【规则 1】如果光标停在 冒号(:) 或 逗号(,) 上，自动向右跳过
            while (caret < currentText.Length && (currentText[caret] == ':' || currentText[caret] == ','))
            {
                caret++;
            }

            // 到达最右侧，无法再输入
            if (caret >= currentText.Length)
            {
                e.Handled = true;
                return;
            }

            // 【规则 2】原地覆盖当前位置的数字
            char[] textChars = currentText.ToCharArray();
            textChars[caret] = inputChar;
            Text = new string(textChars);

            // 【规则 3】将光标移动到刚刚覆盖的数字后面
            CaretIndex = caret + 1;

            // 阻止默认的“插入字符”行为
            e.Handled = true;
        }

        /// <summary>
        /// 处理 Backspace 和 Delete，使它们变成“将数字归零”并移动光标
        /// </summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (SelectionStart != SelectionEnd)
            {
                base.OnKeyDown(e);
                return;
            }

            string currentText = Text ?? "00:00:00,000";
            int caret = CaretIndex;

            if (e.Key == Key.Back)
            {
                // 退格：向左寻找前一个数字，跳过冒号和逗号
                caret--;
                while (caret >= 0 && (currentText[caret] == ':' || currentText[caret] == ','))
                {
                    caret--;
                }

                if (caret >= 0)
                {
                    char[] textChars = currentText.ToCharArray();
                    textChars[caret] = '0'; // 归零
                    Text = new string(textChars);
                    CaretIndex = caret;     // 光标停在变 0 的数字前面
                }
                e.Handled = true; 
            }
            else if (e.Key == Key.Delete)
            {
                // 删除：向右寻找当前要清除的数字
                while (caret < currentText.Length && (currentText[caret] == ':' || currentText[caret] == ','))
                {
                    caret++;
                }

                if (caret < currentText.Length)
                {
                    char[] textChars = currentText.ToCharArray();
                    textChars[caret] = '0';
                    Text = new string(textChars);

                    // 删完后光标自动跳到下一个有效数字前
                    caret++;
                    while (caret < currentText.Length && (currentText[caret] == ':' || currentText[caret] == ','))
                    {
                        caret++;
                    }
                    CaretIndex = caret;
                }
                e.Handled = true;
            }
            else
            {
                base.OnKeyDown(e);
            }
        }
    }
}