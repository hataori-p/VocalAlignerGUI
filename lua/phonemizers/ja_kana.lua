-- ja_kana.lua
-- Japanese Kana Phonemizer for VocalAligner
-- Converts Hiragana/Katakana text to REX-compatible phonemes.
-- Uses the same kana_map.txt data file as Utan's hiragana.lua.
-- Compatible with VocalAligner IPhonemizerProfile contract.

local profile = {}

profile.id           = "ja_kana"
profile.display_name = "Japanese (Hiragana/Katakana)"
profile.encoding     = "IPA"

profile.supported_symbols = {
    "a", "i", "u", "e", "o", "N",
    "k", "s", "sh", "t", "ts", "ch", "n", "h", "f", "m", "y", "r", "w",
    "g", "z", "j", "d", "b", "p",
    "ky", "ny", "hy", "my", "ry", "gy", "by", "py",
    "v", "dy", "ty",
    "_", "pau", "sil", "SP", "cl"
}

local _kana_dict    = nil
local _reverse_dict = nil

local function ensure_dict()
    if _kana_dict == nil then
        _kana_dict = vocal.io.load_dictionary("lua/data/kana_map.txt", "=")
    end
end

local function ensure_reverse_dict()
    if _reverse_dict ~= nil then return end
    _reverse_dict = {}
    local status, content = pcall(vocal.io.read_text, "lua/data/kana_map.txt")
    if status and content then
        for line in content:gmatch("[^\r\n]+") do
            if not line:match("^#") and line:match(".") then
                local kana, phons = line:match("^(.-)=(.+)$")
                if kana and phons then
                    if _reverse_dict[phons] == nil then
                        _reverse_dict[phons] = kana
                    end
                end
            end
        end
    else
        vocal.log("ja_kana: could not load kana_map.txt for P2G: " .. tostring(content))
    end
end

--- Core G2P for a single kana string.
--- Uses grapheme_split for correct Unicode handling.
--- Greedy matching: tries 3 graphemes, then 2, then 1.
local function phonemize_one(text)
    ensure_dict()

    text = text:match("^%s*(.-)%s*$") -- trim
    if text == "" or text == "-" or text == "+" or text == "R" then
        return ""
    end

    local chars  = vocal.text.grapheme_split(text)
    local total  = #chars
    local phonemes = {}
    local i = 1

    while i <= total do
        local matched = false

        -- Greedy: try 3 graphemes, then 2, then 1
        for try_len = 3, 1, -1 do
            if i + try_len - 1 <= total then
                -- Build substring from grapheme array
                local parts = {}
                for j = i, i + try_len - 1 do
                    table.insert(parts, chars[j])
                end
                local sub = table.concat(parts)

                local phon_str = _kana_dict.get(sub)
                if phon_str ~= nil then
                    for p in phon_str:gmatch("%S+") do
                        table.insert(phonemes, p)
                    end
                    i = i + try_len
                    matched = true
                    break
                end
            end
        end

        if not matched then
            -- Small kana (e.g. っ sokuon) or unknown
            local char = chars[i]
            -- っ / ッ = geminate consonant marker
            if char == "っ" or char == "ッ" then
                table.insert(phonemes, "cl")
            else
                -- Unknown: log and skip
                vocal.log("ja_kana: unknown character '" .. char .. "' in '" .. text .. "'")
            end
            i = i + 1
        end
    end

    return table.concat(phonemes, " ")
end

--- Required: g2p_batch
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
function profile.try_p2g(phonemes)
    ensure_reverse_dict()
    local key = table.concat(phonemes, " ")
    return _reverse_dict[key]
end

--- Optional: validate_symbol
function profile.validate_symbol(symbol)
    for _, s in ipairs(profile.supported_symbols) do
        if s == symbol then return true end
    end
    return false
end

return profile
