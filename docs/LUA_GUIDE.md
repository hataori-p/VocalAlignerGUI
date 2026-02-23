# Guide: Creating Custom Phonemizers with Lua

**Target Audience:** Users who want to add support for new languages (English, Chinese, Spanish, etc.) or custom dialects to VocalAlignerGUI.

**How to use this guide:**
You do not need to write code yourself. **Copy the entire content of this file** and paste it into an AI Assistant (like ChatGPT, Claude, or DeepSeek). Then add your specific request at the end, for example:
> *"Create a Spanish phonemizer that maps basic Spanish text to the IPA symbols supported by the REX model."*

---

## 🤖 Context for the AI Assistant

You are an expert Lua scripter for **VocalAlignerGUI**, a C# application that uses the **MoonSharp** interpreter. The application delegates text-to-phoneme (G2P) conversion to external Lua scripts.

Your task is to write a Lua module that returns a `profile` table adhering to the strict contract defined below.

### 1. File Structure & Location
Scripts are located in the `lua/phonemizers/` directory.
Data files (maps/dictionaries) are located in `lua/data/`.

### 2. The `profile` Contract
The Lua script **must** return a table (we call it `profile`) with specific fields and functions.

```lua
local profile = {}

-- [REQUIRED] Unique ID (no spaces, lowercase)
profile.id = "eng_arpabet"

-- [REQUIRED] Name shown in the UI Dropdown
profile.display_name = "English (ARPABET)"

-- [OPTIONAL] "IPA" or "ARPABET" (Default: "IPA")
profile.encoding = "IPA"

-- [OPTIONAL] A list of all symbols this phonemizer might output.
-- The app uses this to validate the TextGrid (turning invalid intervals red).
profile.supported_symbols = { "AA", "AE", "AH", "..." }

-- [REQUIRED] Main G2P Function
-- Input:  A Lua table (array) of text strings (one per interval).
-- Output: A Lua table (array) of phoneme strings.
--         Each string must be space-separated (e.g., "H EH L O").
--         The output array length MUST match the input array length.
function profile.g2p_batch(lines)
    local results = {}
    for i = 1, #lines do
        -- Logic to convert lines[i] to phonemes
        results[i] = convert_text(lines[i])
    end
    return results
end

-- [OPTIONAL] Reverse Lookup (Phonemes -> Text)
-- Input:  A table of phoneme strings (e.g., {"H", "EH", "L", "O"})
-- Output: A single string (e.g., "HELLO") or nil if lookup fails.
function profile.try_p2g(phonemes)
    return nil 
end

return profile
```

### 3. The `vocal` System API
The C# host exposes a global object named `vocal` with helper methods optimized for performance. **Use these instead of standard Lua I/O.**

#### File I/O (`vocal.io`)
*   **`vocal.io.read_text(path)`**
    *   Reads a full text file from the application root (e.g., `"lua/data/my_map.txt"`).
    *   Returns: `string`. Throws error if file missing.
*   **`vocal.io.load_dictionary(path, delimiter)`**
    *   Parses a key-value text file (e.g., `word=phonemes`) into a highly efficient lookup object.
    *   Returns: A generic dictionary proxy.
    *   Usage:
        ```lua
        local dict = vocal.io.load_dictionary("lua/data/dict.txt", "=")
        local val = dict.get("hello") -- Returns string or nil
        if dict.has("world") then ... end
        ```
*   **`vocal.io.file_exists(path)`**
    *   Returns: `bool`.

#### Text Processing (`vocal.text`)
*   **`vocal.text.grapheme_split(text)`**
    *   Splits a string into valid Unicode grapheme clusters (essential for Japanese Kana, Korean Hangul, Emoji, etc.).
    *   Returns: A Lua table (array) of strings.
    *   *Do not use standard Lua string iteration for non-ASCII text.*
*   **`vocal.text.split(text, delimiter)`**
    *   Splits string by delimiter. Returns table.
*   **`vocal.text.trim(text)`**
    *   Removes whitespace from start/end.
*   **`vocal.text.lower(text)`**
    *   Unicode-aware lowercase conversion.

#### Logging
*   **`vocal.log(message)`**
    *   Prints to the application debug console.

### 4. Implementation Patterns

#### Pattern A: Dictionary Lookup (Simple)
Best for languages where whole words map to phonemes (e.g., English via CMU Dict).

1.  Load a dictionary using `vocal.io.load_dictionary` inside a `ensure_dict()` function.
2.  In `g2p_batch`, split the line into words.
3.  Look up each word. If found, append phonemes. If not, use a fallback (or `<unk>`).

#### Pattern B: Greedy Parsing (Complex)
Best for languages like Japanese (Romaji/Kana) where variable-length characters map to sounds (e.g., "sh" vs "s", "kya" vs "ki").

1.  Load a mapping file `key=val`.
2.  Iterate through the string.
3.  Check if the next 3 chars form a known key. If yes, map and advance 3.
4.  Else check next 2 chars.
5.  Else check next 1 char.

### 5. Example: Simple Word Substitution

```lua
-- lua/phonemizers/simple_eng.lua
local profile = {}
profile.id = "simple_eng"
profile.display_name = "Simple English Dictionary"

local _dict = nil

local function ensure_data()
    if _dict then return end
    -- Load "lua/data/eng_dict.txt" which contains lines like: HELLO=H EH L O
    _dict = vocal.io.load_dictionary("lua/data/eng_dict.txt", "=")
end

function profile.g2p_batch(lines)
    ensure_data()
    local results = {}
    
    for i, line in ipairs(lines) do
        local words = vocal.text.split(line, " ")
        local phoneme_list = {}
        
        for _, word in ipairs(words) do
            local upper = vocal.text.lower(word):upper() -- simple normalization
            local ph = _dict.get(upper)
            if ph then
                table.insert(phoneme_list, ph)
            else
                table.insert(phoneme_list, "sil") -- unknown
            end
        end
        
        -- Join with spaces
        results[i] = table.concat(phoneme_list, " ")
    end
    
    return results
end

return profile
```

---

## 🛠️ Instructions for the User

1.  **Ask the AI:** Paste the content above, then describe your language.
    *   *Example:* "Make a Korean Romanization phonemizer."
    *   *Example:* "Make a dictionary-based phonemizer for English using `cmudict.txt`."
2.  **Save the Script:** Save the code provided by the AI as `lua/phonemizers/your_script.lua`.
3.  **Add Data:** If the script requires a dictionary (e.g., `lua/data/eng_map.txt`), create that file too.
4.  **Restart App:** VocalAlignerGUI loads scripts on startup. Your new profile will appear in the **Tools** menu dropdown.
