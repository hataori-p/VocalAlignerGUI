# Controls Reference Guide

This guide details the keyboard shortcuts, mouse interactions, and touch/gesture controls available in **VocalAlignerGUI**.

---

## Touch & On-Screen Keyboard (OSK)

For touch screens and tablets, you can enable the floating On-Screen Keyboard via **View > Show On-Screen Keyboard**. It can be dragged around the screen using the top handle. 

**Virtual Modifiers (Ctrl, Alt, Shift):**
The OSK modifier buttons use a 3-state system:
1. **Gray (Inactive):** Modifier is off.
2. **Blue (One-Shot):** Modifier is active for your *very next touch action*, then automatically turns off.
3. **Red (Locked):** Modifier stays active until you tap it again.

*Note: Active Stylus pens (Surface, Apple Pencil) work natively. The pen tip acts as a Left Click, and the barrel button acts as a Right Click.*

---

## Interactions & Workflows

### Timeline & Navigation (Waveform / Spectrogram)
| Action | Mouse / Keyboard | Touch / OSK | Description |
| :--- | :--- | :--- | :--- |
| **Seek / Move Playhead** | `Left Click` | **Tap** | Moves the red playhead cursor to the clicked/tapped position. |
| **Pan View** | `Left Drag` | **Two-Finger Drag** | Pans the timeline left or right. |
| **Zoom** | `Mouse Wheel` | **Pinch In / Out** | Zooms in/out centered on the mouse/pinch location. |
| **Scroll** | `Shift` + `Wheel` | **Two-Finger Drag** | Scrolls the timeline horizontally. |
| **Range Selection** | `Shift` + `Left Drag` | OSK **Shift** + **Drag** | Selects a time range (highlighted in blue). |
| **Play from Click** | `Right Click` | **Tap** then OSK **Play** | Plays audio from the clicked point to the end of the visible view. |
| **Play to End** | `Shift` + `Right Click` | **Tap** then OSK **Play End** | Plays audio from the clicked point to the very end of the file. |

### Tier Editing (The Bottom Lane)
| Action | Mouse / Keyboard | Touch / OSK | Description |
| :--- | :--- | :--- | :--- |
| **Select Interval** | `Left Click` | **Tap** | Highlights an interval and moves the playhead to its start. |
| **Multi-Select** | `Shift` + `Left Click` | OSK **Shift** + **Tap** | Selects a range of intervals between the previous selection and the click. |
| **Edit Text** | `Double Click` | **Double Tap** | Opens the text editor for the clicked interval. |
| **Split Interval** | `Ctrl` + `Left Drag` | OSK **Ctrl** + **Drag** | Creates a new boundary at the release position (yellow guide appears). |
| **Move Boundary** | `Left Drag` | **1-Finger Drag** | Moves a boundary line. Temporarily locks it while dragging. |
| **Smart Drag / Elastic**| `Alt` + `Left Drag` | OSK **Alt** + **Drag** | **AI Mode:** Scoped re-alignment. **Manual Mode:** Elastic stretch. |
| **Lock / Unlock** | `Double Click` (Line)| **Double Tap** (Line) | Toggles a boundary between **Fluid (Blue)** and **Locked (Red)**. |
| **Delete Boundary** | `Ctrl` + `Right Click`| **Tap** then OSK **Del** | Deletes the boundary at the playhead and merges adjacent intervals. |
| **Play Interval** | `Right Click` | **Tap** then OSK **Play Int**| Plays the specific audio range of the tapped interval. |
| **Context Menu** | `Ctrl` + `Right Click`| **Long-Press** | Opens menu options (Rename, etc.) on an interval. |

---

## Keyboard Shortcuts & OSK Actions

### Playback
| Key | OSK Button | Function |
| :--- | :--- | :--- |
| **Space** | **Play / Stop** | Play / Pause (toggles playback). |
| **Shift + Space**| **Play Vis** | Play the currently visible screen area. |
| **Escape** | **Esc** | Stop playback. If already stopped, clears the current selection. |

*Note: Tapping any Transport button on the OSK while audio is playing will instantly stop playback.*

### Navigation & View
| Key | OSK Button | Function |
| :--- | :--- | :--- |
| **Up Arrow** | - | Zoom In. |
| **Down Arrow** | - | Zoom Out. |
| **Page Up** | - | Scroll Left (Previous page). |
| **Page Down** | - | Scroll Right (Next page). |
| **Home** | - | Go to Start of file. |
| **End** | - | Go to End of file. |
| **C** | **C (Ctr)** | Center view on the Playhead cursor. |
| **Z** | **Z (Sel)** | **Zoom to Selection**: Fills the screen with the selected area, then clears the selection highlight. |
| **Ctrl + Left** | - | Jump cursor to previous interval start. |
| **Ctrl + Right** | - | Jump cursor to next interval start. |

### Editing & Tools
| Key | Function |
| :--- | :--- |
| **Ctrl + S** | Save the current TextGrid. |
| **Ctrl + V** | Paste text from clipboard into the interval under the playhead (smart formatting applied). |
| **Ctrl + Shift + I**| **Insert Silence**: Splits the selected range into `Text` → `Silence (_)` → `Text`. |

### Search / Find
| Key | Function |
| :--- | :--- |
| **Ctrl + F** | Open the Find/Search overlay. |
| **F3** | Find Next occurrence. |
| **Shift + F3** | Find Previous occurrence. |
| **Esc** | Close Search overlay. |

---

## Visual Guide
*   <span style="color:blue">**Blue Lines:**</span> **Fluid Boundaries.** These are estimates placed by the AI or user. They will be moved automatically during a global "Realign".
*   <span style="color:red">**Red Lines:**</span> **Locked Anchors.** These are pinned constraints. The AI will *never* move a red line.
*   <span style="color:yellow">**Yellow Line:**</span> **Drag Guide.** Appears only while dragging to show where a split or boundary will land.
*   <span style="color:red">**Red Text:**</span> Indicates an **Invalid Phoneme** that is not supported by the current acoustic model. Use the "Convert Selection" tool to fix.
