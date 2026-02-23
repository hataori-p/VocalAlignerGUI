-- ja_romaji.lua
-- Japanese Romaji Phonemizer for VocalAligner
-- Converts Romaji text (e.g. "konnichiwa") to REX-compatible phonemes (e.g. "k o n n i ch i w a")
-- Compatible with VocalAligner IPhonemizerProfile contract.

local profile = {}

profile.id           = "ja_romaji"
profile.display_name = "Japanese (Romaji)"
profile.encoding     = "IPA"

-- Supported phoneme symbols (used by ValidateGrid)
profile.supported_symbols = {
    "a", "i", "u", "e", "o", "N",
    "k", "s", "sh", "t", "ts", "ch", "n", "h", "f", "m", "y", "r", "w",
    "g", "z", "j", "d", "b", "p",
    "ky", "ny", "hy", "my", "ry", "gy", "by", "py",
    "v", "dy", "ty",
    "_", "pau", "sil", "SP", "cl"
}

-- Lazy-loaded dictionary (C#-backed O(1) lookup)
local _romaji_dict = nil

local function ensure_dict()
    if _romaji_dict == nil then
        _romaji_dict = vocal.io.load_dictionary("lua/data/romaji_map.txt", "=")
    end
end

-- Reverse map for P2G (built once)
local _reverse_dict = nil

local function ensure_reverse_dict()
    if _reverse_dict ~= nil then return end
    _reverse_dict = {}
    local status, content = pcall(vocal.io.read_text, "lua/data/romaji_map.txt")
    if status and content then
        for line in content:gmatch("[^\r\n]+") do
            if not line:match("^#") and line:match(".") then
                local romaji, phons = line:match("^(.-)=(.+)$")
                if romaji and phons then
                    -- Only store first mapping (prefer common form)
                    if _reverse_dict[phons] == nil then
                        _reverse_dict[phons] = romaji
                    end
                end
            end
        end
    else
        vocal.log("ja_romaji: could not load romaji_map.txt for P2G: " .. tostring(content))
    end
end

--- Greedy Romaji splitter.
--- e.g. "konnichiwa" -> {"ko", "n", "ni", "chi", "wa"}
--- Handles sokuon (double consonants): "kitte" -> {"ki", "t", "te"}
local function split_romaji(text)
    text = text:lower()
    local result = {}
    local i = 1
    local len = #text

    while i <= len do
        local matched = false

        -- Try longest match first: 4, 3, 2, 1
        for try_len = 4, 1, -1 do
            if i + try_len - 1 <= len then
                local candidate = text:sub(i, i + try_len - 1)
                if _romaji_dict.has(candidate) then
                    table.insert(result, candidate)
                    i = i + try_len
                    matched = true
                    break
                end
            end
        end

        if not matched then
            -- Sokuon: double consonant (e.g. "tt" in "kitte")
            -- Skip the first consonant; the phonemizer will emit "cl" for it
            if i + 1 <= len then
                local c1 = text:sub(i, i)
                local c2 = text:sub(i + 1, i + 1)
                local vowels = { a=true, i=true, u=true, e=true, o=true }
                if c1 == c2 and not vowels[c1] then
                    table.insert(result, "cl") -- geminate consonant marker
                    i = i + 1 -- skip first of the pair
                else
                    -- Unknown character: pass through as-is
                    table.insert(result, c1)
                    i = i + 1
                end
            else
                table.insert(result, text:sub(i, i))
                i = i + 1
            end
        end
    end

    return result
end

--- Core G2P for a single text string.
--- Returns space-separated phoneme string, e.g. "k o n n i ch i w a"
local function phonemize_one(text)
    ensure_dict()

    -- Handle special/empty cases
    text = text:match("^%s*(.-)%s*$") -- trim
    if text == "" or text == "-" or text == "+" or text == "R" then
        return ""
    end

    -- If it's already a known phoneme token (e.g. "pau", "sil", "_")
    if _romaji_dict.has(text) then
        local direct = _romaji_dict.get(text)
        if direct ~= nil then
            return direct
        end
    end

    local morae = split_romaji(text)
    local phonemes = {}

    for _, mora in ipairs(morae) do
        if mora == "cl" then
            table.insert(phonemes, "cl")
        else
            local phon_str = _romaji_dict.get(mora)
            if phon_str ~= nil then
                -- phon_str is like "k a" or "sh i" - split and add each
                for p in phon_str:gmatch("%S+") do
                    table.insert(phonemes, p)
                end
            else
                -- Unknown mora: pass through
                table.insert(phonemes, mora)
            end
        end
    end

    return table.concat(phonemes, " ")
end

--- Required: g2p_batch
--- Input:  Lua table of strings (one per interval)
--- Output: Lua table of strings (space-separated phonemes, one per interval)
function profile.g2p_batch(lines)
    ensure_dict()
    local results = {}
    for i = 1, #lines do
        local text = lines[i]
        if type(text) == "string" then
            results[i] = phonemize_one(text)
        else
            results[i] = ""
        end
    end
    return results
end

--- Optional: try_p2g
--- Input:  Lua table of phoneme strings e.g. {"k", "a"}
--- Output: Romaji string e.g. "ka", or nil if not found
function profile.try_p2g(phonemes)
    ensure_reverse_dict()
    -- Build key from phoneme list
    local key = table.concat(phonemes, " ")
    return _reverse_dict[key] -- nil if not found
end

--- Optional: validate_symbol
--- Returns true if symbol is in the supported set
function profile.validate_symbol(symbol)
    for _, s in ipairs(profile.supported_symbols) do
        if s == symbol then return true end
    end
    return false
end

return profile
