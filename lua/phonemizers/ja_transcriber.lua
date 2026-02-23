-- ja_transcriber.lua
-- Japanese Transcriber for VocalAligner
-- Converts raw Romaji text to Stable IPA phoneme sequences.
-- Port of: allosaurus_rex/phonemizer/implementations/japanese/transcriber.py
-- Input:  table of text strings, one per interval (e.g. "konnichiwa")
-- Output: table of space-separated IPA strings (e.g. "k o n n i tɕ i w a")

local profile = {}

profile.id           = "ja_transcriber"
profile.display_name = "Japanese Transcriber (Romaji → IPA)"
profile.encoding     = "IPA"

-- Declare supported output symbols (stable IPA used by REX model)
-- This is read by C# for ValidateGrid()
profile.supported_symbols = {
    "a", "i", "ɯ", "e", "o",           -- vowels
    "k", "ɡ", "s", "z", "t", "d",      -- stops/fricatives
    "n", "h", "ɸ", "b", "p", "m",      -- nasals/bilabials
    "j", "ɾ", "w", "v", "ʔ",           -- approximants/glottal
    "ɕ", "tɕ", "ts", "dʑ",             -- affricates/palatals
    "kʲ", "ɡʲ", "ç", "bʲ", "pʲ",      -- palatalized
    "mʲ", "nʲ", "ɾʲ",                  -- palatalized cont.
    "ɴ", "br", "sil", "sp", "_"        -- special/boundary
}

-- Stable IPA vowel set (from STABLE_IPA_VOWELS in tables.py)
local IPA_VOWELS = {
    ["a"]=true, ["i"]=true, ["ɯ"]=true, ["e"]=true, ["o"]=true, ["ɴ"]=true
}

-- Romaji vowels (for tokenizer context)
local ROMAJI_VOWELS = {
    ["a"]=true, ["i"]=true, ["u"]=true, ["e"]=true, ["o"]=true
}

-- The known Romaji multi-character tokens (from ROMAJI_PHONEME_SET in tables.py)
-- Listed longest-first to support greedy matching
local ROMAJI_TOKENS = {
    "ssh", "cch",
    "ky", "gy", "sh", "ch", "ts", "zj", "dz", "ty", "dy", "hy", "by", "py",
    "my", "ny", "ry", "dj", "br",
    "a", "i", "u", "e", "o",
    "k", "g", "s", "z", "t", "d", "n", "h", "f",
    "b", "p", "m", "y", "r", "l", "w", "v",
    "N", "'", "q", "-", "_"
}

-- Passthrough tokens: silence markers and IPA symbols passed as-is
local PASSTHROUGH = {
    ["_"]=true, ["sil"]=true, ["sp"]=true, ["pau"]=true, ["br"]=true
}

-- Romaji to IPA map (loaded from file, mirrors ROMAJI_TO_IPA_MAP)
local _romaji_dict = nil

-- Stable IPA set (for passthrough detection)
-- Built from romaji_to_ipa.txt values
local _stable_ipa_set = nil

local function ensure_dict()
    if _romaji_dict ~= nil then return end
    _romaji_dict = vocal.io.load_dictionary("lua/data/romaji_to_ipa.txt", "=")

    -- Build stable IPA set from the values
    _stable_ipa_set = {}
    local status, content = pcall(vocal.io.read_text, "lua/data/romaji_to_ipa.txt")
    if status and content then
        for line in content:gmatch("[^\r\n]+") do
            local first = line:sub(1, 1)
            if first ~= "#" and first ~= "" then
                local eq = line:find("=", 1, true)
                if eq then
                    local ipa = line:sub(eq + 1)
                    if ipa and ipa ~= "" then
                        _stable_ipa_set[ipa] = true
                    end
                end
            end
        end
    end
    -- Manually add known stable IPA symbols not in the map values
    -- (These are symbols that may appear as-is in input)
    local extra = {
        "a","i","ɯ","e","o","ɴ","k","ɡ","s","z","t","d","n","h","ɸ",
        "b","p","m","j","ɾ","w","v","ʔ","br","ɕ","tɕ","ts","dʑ",
        "kʲ","ɡʲ","ç","bʲ","pʲ","mʲ","nʲ","ɾʲ","sil"
    }
    for _, sym in ipairs(extra) do
        _stable_ipa_set[sym] = true
    end
end

--- Hybrid longest-match tokenizer.
--- Port of JapaneseTranscriber._tokenize()
--- Handles both Romaji and already-IPA input tokens.
local function tokenize(text)
    text = text:lower()
    local tokens = {}
    local i = 1
    local len = #text

    while i <= len do
        -- Skip whitespace
        if text:sub(i,i):match("%s") then
            i = i + 1
        else
            local matched = false

            -- Try longest Romaji tokens first
            for _, token in ipairs(ROMAJI_TOKENS) do
                local tlen = #token
                if i + tlen - 1 <= len then
                    local candidate = text:sub(i, i + tlen - 1)
                    if candidate == token then
                        table.insert(tokens, token)
                        i = i + tlen
                        matched = true
                        break
                    end
                end
            end

            if not matched then
                -- Try to emit the character as a single token (handles IPA passthrough)
                -- Use vocal.text.grapheme_split to safely get one grapheme
                local chars = vocal.text.grapheme_split(text:sub(i))
                local first = chars and #chars > 0 and chars[1] or nil
                if first then
                    -- Check if it is a known stable IPA symbol
                    if _stable_ipa_set and _stable_ipa_set[first] then
                        table.insert(tokens, first)
                    else
                        vocal.log("ja_transcriber: unknown char '" .. first .. "' in '" .. text .. "'")
                        table.insert(tokens, "sil")
                    end
                    -- Advance by byte length of the grapheme
                    i = i + #first
                else
                    i = i + 1
                end
            end
        end
    end

    return tokens
end

--- Contextual rule application.
--- Port of JapaneseTranscriber._apply_contextual_rules()
local function apply_contextual_rules(tokens)
    ensure_dict()
    local ipa = {}
    local i = 1

    while i <= #tokens do
        local token = tokens[i]
        local next_token = tokens[i + 1]

        -- Rule: 'n' (moraic nasal)
        -- Before vowel → "n", before consonant/end → "ɴ"
        if token == "n" then
            local is_vowel_next = next_token and
                (ROMAJI_VOWELS[next_token] or IPA_VOWELS[next_token])
            if is_vowel_next then
                table.insert(ipa, "n")
            else
                table.insert(ipa, "ɴ")
            end
            i = i + 1

        -- Rule: 'h' before 'i' → palatalized 'ç'
        elseif token == "h" and next_token == "i" then
            table.insert(ipa, "ç")
            -- NOTE: do NOT skip 'i' — it will be processed in next iteration
            i = i + 1

        -- Rule: '-' (long vowel marker)
        elseif token == "-" then
            if #ipa > 0 and IPA_VOWELS[ipa[#ipa]] then
                table.insert(ipa, ipa[#ipa]) -- repeat last vowel
            else
                table.insert(ipa, "sil")
            end
            i = i + 1

        -- Rule: 'q' (gemination)
        elseif token == "q" and next_token then
            local ipa_next = _romaji_dict.get(next_token)
            if ipa_next then
                -- Geminate: emit first character of next IPA symbol
                table.insert(ipa, ipa_next:sub(1,1))
            end
            i = i + 1

        -- Rule: apostrophe (pitch accent marker in some Romaji systems) → silent, skip
        elseif token == "'" then
            -- consume without emitting any phoneme
            i = i + 1

        -- General case: lookup in ROMAJI_TO_IPA, fallback to stable IPA, fallback to sil
        else
            -- Explicit passthrough for boundary/silence tokens
            if PASSTHROUGH[token] then
                table.insert(ipa, token)
            else
                local ipa_sym = _romaji_dict.get(token)
                if ipa_sym then
                    table.insert(ipa, ipa_sym)
                elseif _stable_ipa_set and _stable_ipa_set[token] then
                    table.insert(ipa, token)
                else
                    vocal.log("ja_transcriber: no IPA for token '" .. token .. "'")
                    table.insert(ipa, "sil")
                end
            end
            i = i + 1
        end
    end

    return ipa
end

-- Simple trim without lazy quantifier (avoids MoonSharp pattern complexity limit)
local function trim(s)
    local i = 1
    while i <= #s and s:sub(i,i):match("%s") do i = i + 1 end
    local j = #s
    while j >= i and s:sub(j,j):match("%s") do j = j - 1 end
    return s:sub(i, j)
end

--- Core G2P for a single text string.
local function transcribe_one(text)
    ensure_dict()
    text = trim(text)
    if text == "" then return "" end

    -- If input is a single passthrough token, return it as-is
    if PASSTHROUGH[text] then return text end

    -- If input looks like already-IPA (contains IPA chars, single token, no romaji)
    -- pass through with a warning rather than crashing
    if _stable_ipa_set and _stable_ipa_set[text] then
        vocal.log("ja_transcriber: passthrough stable IPA token: '" .. text .. "'")
        return text
    end

    local tokens = tokenize(text)
    local ipa    = apply_contextual_rules(tokens)

    return table.concat(ipa, " ")
end

--- Required: g2p_batch
--- Input:  table of strings (one per interval)
--- Output: table of space-separated IPA strings
function profile.g2p_batch(lines)
    local results = {}
    for i = 1, #lines do
        local text = lines[i]
        results[i] = (type(text) == "string") and transcribe_one(text) or ""
    end
    return results
end

--- Optional: validate_symbol
function profile.validate_symbol(symbol)
    ensure_dict()
    return (_stable_ipa_set ~= nil and _stable_ipa_set[symbol] == true)
end

return profile
