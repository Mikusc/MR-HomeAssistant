using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MRHomeAssistant.Runtime.Presentation.UI
{
    internal sealed class DashboardKeyboard
    {
        public RectTransform Root { get; }
        public bool IsOpen => Root.gameObject.activeSelf;
        private TMP_InputField target;
        private readonly TMP_InputField editor;
        private readonly TMP_Text title;
        private readonly Button[] keys = new Button[40];
        private bool symbols;
        private bool upper;
        private const string Letters = "1234567890qwertyuiopasdfghjkl-zxcvbnm./:";
        private const string Symbols = "1234567890!@#$%^&*()_+-=[]{};:'\"\\|,.?/~";

        public DashboardKeyboard(Transform parent, Action closed)
        {
            Root = DashboardUI.Rect(parent, "Text entry", 0, 150, 1000, 504);
            DashboardUI.Fill(Root, DashboardUI.Background, true);
            title = DashboardUI.Label(Root, "Editing", "Text entry", 32, 12, 640, 40, 26);
            editor = DashboardUI.Input(Root, "Keyboard value", "", 32, 64, 936);
            editor.characterLimit = 4096;
            editor.onFocusSelectAll = false;
            for (int i = 0; i < keys.Length; i++)
            {
                int index = i;
                keys[i] = DashboardUI.Button(Root, "Key " + i, "", 32 + i % 10 * 94, 134 + i / 10 * 64, 86, 56,
                    () => Insert(Key(index)));
            }
            DashboardUI.Button(Root, "Shift", "Shift", 32, 398, 110, 56, () => { upper = !upper; RefreshKeys(); });
            DashboardUI.Button(Root, "Symbols", "#+=", 150, 398, 102, 56, () => { symbols = !symbols; RefreshKeys(); });
            DashboardUI.Button(Root, "Space", "Space", 260, 398, 224, 56, () => Insert(" "));
            DashboardUI.Button(Root, "Backspace", "Delete", 492, 398, 142, 56, () =>
            {
                int start = Math.Min(editor.selectionStringAnchorPosition, editor.selectionStringFocusPosition);
                int end = Math.Max(editor.selectionStringAnchorPosition, editor.selectionStringFocusPosition);
                if (end > start) Replace(start, end, "");
                else if (start > 0) Replace(start - 1, start, "");
            });
            DashboardUI.Button(Root, "Paste", "Paste", 642, 398, 132, 56, () => Insert(GUIUtility.systemCopyBuffer ?? ""));
            DashboardUI.Button(Root, "Done", "Done", 782, 398, 186, 56, () => { Commit(); closed(); }, true);
            DashboardUI.Button(Root, "Cancel", "Cancel", 798, 8, 170, 44, () => { Close(); closed(); });
            Root.gameObject.SetActive(false);
            RefreshKeys();
        }

        public void Open(TMP_InputField field)
        {
            target = field;
            title.text = field.gameObject.name;
            editor.contentType = field.contentType;
            editor.SetTextWithoutNotify(field.text);
            Root.gameObject.SetActive(true);
            Root.SetAsLastSibling();
            editor.ActivateInputField();
            editor.stringPosition = editor.text.Length;
        }

        public void Close()
        {
            editor.DeactivateInputField();
            editor.SetTextWithoutNotify("");
            target = null;
            Root.gameObject.SetActive(false);
        }

        private void Commit()
        {
            if (target != null) target.text = editor.text;
            Close();
        }

        private string Key(int index)
        {
            string source = symbols ? Symbols : Letters;
            if (index >= source.Length) return "";
            return upper ? char.ToUpperInvariant(source[index]).ToString() : source[index].ToString();
        }

        private void RefreshKeys()
        {
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].GetComponentInChildren<TMP_Text>().text = Key(i);
                keys[i].interactable = Key(i).Length > 0;
            }
        }

        private void Insert(string value)
        {
            Replace(Math.Min(editor.selectionStringAnchorPosition, editor.selectionStringFocusPosition),
                Math.Max(editor.selectionStringAnchorPosition, editor.selectionStringFocusPosition), value);
        }

        private void Replace(int start, int end, string value)
        {
            start = Mathf.Clamp(start, 0, editor.text.Length);
            end = Mathf.Clamp(end, start, editor.text.Length);
            string text = editor.text.Remove(start, end - start).Insert(start, value);
            int limit = target != null ? target.characterLimit : 4096;
            editor.text = text.Length > limit ? text.Substring(0, limit) : text;
            editor.ActivateInputField();
            editor.stringPosition = Mathf.Min(start + value.Length, editor.text.Length);
        }
    }
}
