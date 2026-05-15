using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace JSI
{
    public class TextMenu : List<TextMenu.Item>
    {
        public int currentSelection;
        public string labelColor = JUtil.ColorToColorTag(Color.white);
        public string rightTextColor = JUtil.ColorToColorTag(Color.cyan);
        public string selectedColor = JUtil.ColorToColorTag(Color.green);
        public string disabledColor = JUtil.ColorToColorTag(Color.gray);
        public string menuTitle = string.Empty;
        public int rightColumnWidth;

        // Tracked items can dynamically change from external inputs, and have more functionality for responding to button presses
        private readonly List<TrackedMenuItem> trackedItems = new List<TrackedMenuItem>();

        // Adds a dynamic item that can change its enabled and selected states
        public void AddMenuItem(string label, Action action,
            Func<bool> enabledCheck = null,
            Func<bool> selectedCheck = null)
        {
            Action<int, TextMenu.Item> menuAction = null;
            if (action != null)
            {
                menuAction = (idx, menuItem) => action();
            }

            var newItem = new TextMenu.Item(label, menuAction);
            Add(newItem);

            if (enabledCheck != null)
            {
                trackedItems.Add(new TrackedMenuItem
                {
                    item = newItem,
                    id = label,
                    isEnabled = enabledCheck,
                    isSelected = selectedCheck,
                });
            }
        }

        // Overload for dynamic labels that update on refresh
        public void AddMenuItem(Func<string> labelFunc, Action action = null,
            Func<bool> enabledCheck = null)
        {
            string initialLabel = labelFunc();
            Action<int, TextMenu.Item> menuAction = null;
            if (action != null)
            {
                menuAction = (idx, menuItem) => action();
            }

            var newItem = new TextMenu.Item(initialLabel, menuAction);
            Add(newItem);

            trackedItems.Add(new TrackedMenuItem
            {
                item = newItem,
                id = "DynamicLabel_" + initialLabel,
                isEnabled = enabledCheck,
                getLabel = labelFunc
            });
        }

        // Adds an item that can toggle a boolean value on or off
        public void AddToggleItem(string label,
            Func<bool> getValue, Action<bool> setValue,
            Func<bool> enabledCheck = null)
        {
            Action<int, TextMenu.Item> toggleAction = (idx, menuItem) =>
            {
                bool current = getValue();
                setValue(!current);
                UpdateTrackedItems();
            };

            // Use color highlighting for toggles - green when enabled, normal when disabled
            // No checkbox prefix needed since RPM interprets [text] as color tags
            var newItem = new TextMenu.Item(label, toggleAction);
            Add(newItem);

            trackedItems.Add(new TrackedMenuItem
            {
                item = newItem,
                id = label,
                isEnabled = enabledCheck,
                isSelected = getValue  // This makes the item green when checked
            });
        }

        // Adds an item that toggles a boolean field within an object on or off
        public void AddToggleItem(string label,
            object obj, FieldInfo field,
            Func<bool> enabledCheck = null)
        {
            AddToggleItem(label,
                () => (bool)field.GetValue(obj),
                val => field.SetValue(obj, val),
                enabledCheck);
        }

        // Adds an item that can edit a numeric value, via IncrementCurrentValue - which you should call from button handlers
        public void AddNumericItem(string label,
            Func<double> getValue, Action<double> setValue,
            double step, Func<double, string> format,
            Func<bool> enabledCheck = null,
            bool hasMin = false, double min = 0,
            bool hasMax = false, double max = 0)
        {
            var newItem = new TextMenu.Item(label);
            Add(newItem);

            trackedItems.Add(new TrackedMenuItem
            {
                item = newItem,
                id = label,
                isEnabled = enabledCheck,
                isValueItem = true,
                getNumber = getValue,
                setNumber = setValue,
                step = step,
                hasMin = hasMin,
                min = min,
                hasMax = hasMax,
                max = max,
                getLabel = () => label + ": " + format(getValue())
            });
        }

        // Call this from a button handler to update the value of a tracked numeric item
        public void IncrementCurrentValue(int direction)
        {
            TextMenu.Item currentItem = GetCurrentItem();
            if (currentItem == null) return;

            for (int i = 0; i < trackedItems.Count; i++)
            {
                TrackedMenuItem tracked = trackedItems[i];
                if (tracked.item == currentItem && tracked.isValueItem && tracked.getNumber != null && tracked.setNumber != null)
                {
                    double current = tracked.getNumber();
                    double next = current + (tracked.step * direction);

                    if (tracked.hasMin && next < tracked.min) next = tracked.min;
                    if (tracked.hasMax && next > tracked.max) next = tracked.max;

                    tracked.setNumber(next);
                    UpdateTrackedItems();
                    break;
                }
            }
        }

        // Call this in Update to refresh all tracked items
        public void UpdateTrackedItems()
        {
            foreach (var tracked in trackedItems)
            {
                try
                {
                    // Update enabled state
                    if (tracked.isEnabled != null)
                    {
                        tracked.item.isDisabled = !tracked.isEnabled();
                    }

                    // Update label
                    if (tracked.getLabel != null)
                    {
                        string newLabel = tracked.getLabel();
                        if (!string.IsNullOrEmpty(newLabel))
                        {
                            tracked.item.labelText = newLabel;
                        }
                    }

                    // Update selected state (for toggles)
                    if (tracked.isSelected != null)
                    {
                        tracked.item.isSelected = tracked.isSelected();
                    }
                }
                catch (Exception)
                {
                    // Silently ignore - keep existing label
                }
            }
        }

        public string ShowMenu(int width, int height)
		{
			var menuString = new StringBuilder();
			ShowMenu(menuString, width, height);
			return menuString.ToString();
		}

		public virtual void ShowMenu(StringBuilder menuString, int width, int height)
        {
            if (!string.IsNullOrEmpty(menuTitle))
            {
                menuString.AppendLine(menuTitle);
                --height;
            }

            // figure out which entries are visible.
            int numEntries = Count;
            // Sanity check: clamp the current selection
            currentSelection = Math.Min(currentSelection, numEntries - 1);

            // Pick the half-way point of the list
            int midPoint = height >> 1;

            int firstPoint;
            if (midPoint > currentSelection)
            {
                // Menu entry is near the top of the list
                firstPoint = 0;
            }
            else if ((currentSelection + height - midPoint) >= numEntries)
            {
                // Menu entry is near the end of the list.  Account for short
                // lists by clamping to zero.
                firstPoint = Math.Max(0, numEntries - height);
            }
            else
            {
                // Long list, current selection is not near the middle
                firstPoint = currentSelection - midPoint;
            }

            int endPoint = Math.Min(firstPoint + height, numEntries);
            // -2 to account for the first column '  ' or '> ' characters
            int textWidth = width - rightColumnWidth - 2;

            var textItem = new StringBuilder();
            for (int index = firstPoint; index < endPoint; ++index)
            {
                // Clear the string builder
                int strLen = textItem.Length;
                textItem.Remove(0, strLen);

                // Add color strings
                textItem.Append(labelColor);
                if (index == currentSelection)
                {
                    textItem.Append("> ");
                }
                else
                {
                    textItem.Append("  ");
                }
                if (this[index].isDisabled)
                {
                    textItem.Append(disabledColor);
                }
                else if (this[index].isSelected)
                {
                    textItem.Append(selectedColor);
                }

                if (!string.IsNullOrEmpty(this[index].labelText))
                {
                    textItem.Append(this[index].labelText.PadRight(textWidth).Substring(0, textWidth));

                    // Only allow a 'right text' to be added if we already have text.
                    if (!string.IsNullOrEmpty(this[index].rightText) && rightColumnWidth > 0)
                    {
                        if (!this[index].isDisabled && !this[index].isSelected)
                        {
                            textItem.Append(rightTextColor);
                        }

                        textItem.Append(this[index].rightText.PadLeft(rightColumnWidth).Substring(0, rightColumnWidth));
                    }
                }

                menuString.AppendLine(textItem.ToString());
            }
        }

        public void NextItem()
        {
            currentSelection = (currentSelection + 1) % Count;
        }

        public void PreviousItem()
        {
            currentSelection = (currentSelection + Count - 1) % Count;
        }

        public void SelectItem()
        {
            // Do callback
            if (!this[currentSelection].isDisabled && this[currentSelection].action != null)
            {
                this[currentSelection].action(currentSelection, this[currentSelection]);
            }
        }

        public Item GetCurrentItem()
        {
            return this[currentSelection];
        }

        public int GetCurrentIndex()
        {
            return currentSelection;
        }

        // Set the isSelected flag for the index menu item.  If "exclusive"
        // is set, all other isSelected flags are cleared.
        public void SetSelected(int index, bool exclusive)
        {
            if (exclusive)
            {
                for (int i = 0; i < Count; ++i )
                {
                    this[i].isSelected = false;
                }
            }

            if (index >= 0 && index < Count)
            {
                this[index].isSelected = true;
            }
        }

        public class Item
        {
            public string labelText = string.Empty;
            public string rightText = string.Empty;
            public int id;
            public bool isDisabled;
            public bool isSelected;
            public Action<int, Item> action;
            // Mihara: This can be much more terse to use if there is a constructor with optional parameters.
            // Even if it's finicky about "" rather than string.Empty.
            public Item(string labelText = "", Action<int, Item> action = null, bool isSelected = false, string rightText = "", bool isDisabled = false)
            {
                this.labelText = labelText;
                this.rightText = rightText;
                this.action = action;
                this.isDisabled = isDisabled;
                this.isSelected = isSelected;
                this.id = -1;
            }
            // Consolidated/simple constructor - set most things to their
            // defaults, and require three fields to be supplied.
            public Item(string labelText, Action<int, Item> action, int id)
            {
                this.labelText = labelText;
                this.rightText = "";
                this.action = action;
                this.isDisabled = false;
                this.isSelected = false;
                this.id = id;
            }
        }

        internal class TrackedMenuItem
        {
            public TextMenu.Item item;
            public string id;
            public Func<bool> isEnabled;
            public Func<bool> isSelected;
            public Func<string> getLabel;
            public Func<string> getValue;
            public bool isValueItem;
            public Func<double> getNumber;
            public Action<double> setNumber;
            public double step;
            public bool hasMin;
            public double min;
            public bool hasMax;
            public double max;
        }
    }
}
