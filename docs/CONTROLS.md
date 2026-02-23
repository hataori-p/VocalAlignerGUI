# Controls Reference Guide

This guide details the keyboard shortcuts and mouse interactions available in **VocalAlignerGUI**.

---

## Mouse Interactions

### Timeline & Navigation (Waveform / Spectrogram)
| Action | Gesture | Description |
| :--- | :--- | :--- |
| **Seek / Move Playhead** | `Left Click` | Moves the red playhead cursor to the clicked position. |
| **Pan View** | `Left Drag` | Click and drag left/right to scroll the timeline. |
| **Range Selection** | `Shift` + `Left Drag` | Selects a time range (highlighted in blue). |
| **Zoom** | `Mouse Wheel` | Zooms in/out centered on the mouse cursor. |
| **Scroll** | `Shift` + `Mouse Wheel` | Scrolls the timeline horizontally. |
| **Play from Click** | `Right Click` | Plays audio from the clicked point to the end of the visible view. |
| **Play to End** | `Shift` + `Right Click` | Plays audio from the clicked point to the very end of the file. |

### Tier Editing (The Bottom Lane)
| Action | Gesture | Description |
| :--- | :--- | :--- |
| **Select Interval** | `Left Click` | Highlights an interval and moves the playhead to its start. |
| **Multi-Select** | `Shift` + `Left Click` | Selects a range of intervals between the previous selection and the click. |
| **Edit Text** | `Double Click` | Opens the text editor for the clicked interval. |
| **Split Interval** | `Ctrl` + `Left Click` | **(On Empty Space)** Creates a new boundary at the clicked position. |
| **Move Boundary** | `Left Drag` | Moves a boundary line. Temporarily locks it while dragging. |
| **Smart Drag / Elastic** | `Alt` + `Left Drag` | **AI Mode:** Triggers a scoped re-alignment between anchors.<br>**Manual Mode:** Proportional "elastic" stretch of internal phonemes. |
| **Lock / Unlock** | `Double Click` (Line) | Toggles a boundary between **Fluid (Blue)** and **Locked (Red)**. |
| **Delete Boundary** | `Ctrl` + `Right Click` | **(On Line)** Deletes the boundary and merges adjacent intervals. |
| **Context Menu** | `Ctrl` + `Right Click` | **(On Interval)** Opens menu options (Rename, etc.). |
| **Play Interval** | `Right Click` | Plays the specific audio range of the clicked interval. |

---

## Keyboard Shortcuts

### Playback
| Key | Function |
| :--- | :--- |
| **Space** | Play / Pause (toggles playback). |
| **Shift + Space** | Play the currently visible screen area. |
| **Escape** | Stop playback. If stopped, clears current selection. |

### Navigation & View
| Key | Function |
| :--- | :--- |
| **Up Arrow** | Zoom In. |
| **Down Arrow** | Zoom Out. |
| **Page Up** | Scroll Left (Previous page). |
| **Page Down** | Scroll Right (Next page). |
| **Home** | Go to Start of file. |
| **End** | Go to End of file. |
| **C** | Center view on the Playhead cursor. |
| **Z** | **Zoom to Selection**: Fills the screen with the selected area, then clears the selection highlight. |
| **Ctrl + Left** | Jump cursor to previous interval start. |
| **Ctrl + Right** | Jump cursor to next interval start. |

### Editing & Tools
| Key | Function |
| :--- | :--- |
| **Ctrl + S** | Save the current TextGrid. |
| **Ctrl + V** | Paste text from clipboard into the interval under the playhead (smart formatting applied). |
| **Ctrl + Shift + I** | **Insert Silence**: Splits the selected range into `Text` → `Silence (_)` → `Text`. |
| **Delete** | (In Context Menu) Delete selected boundary. |

### Search / Find
| Key | Function |
| :--- | :--- |
| **Ctrl + F** | Open the Find/Search overlay. |
| **F3** | Find Next occurrence. |
| **Shift + F3** | Find Previous occurrence. |
| **Esc** | Close Search overlay. |

---

## Visual Guide
*   <span style="color:blue">**Blue Lines:**</span> **Fluid Boundaries.** These are rough estimates placed by the AI or the user. They will be moved automatically during a global "Realign".
*   <span style="color:red">**Red Lines:**</span> **Locked Anchors.** These are pinned constraints. The AI will *never* move a red line.
*   <span style="color:yellow">**Yellow Line:**</span> **Drag Guide.** Appears only while dragging to show where a split or boundary will land.
*   <span style="color:red">**Red Text:**</span> Indicates an **Invalid Phoneme** that is not supported by the current acoustic model. Use the "Convert Selection" tool to fix.
