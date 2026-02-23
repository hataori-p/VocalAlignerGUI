-- Converts Stable IPA → Romaji (1:1) for Japanese.

local profile = {}

profile.id           = "ja_ipa_to_romaji"
profile.display_name = "Japanese (IPA → Romaji)"
profile.encoding     = "IPA"

-- Passthrough symbols (unchanged in output)
local PASSTHROUGH = {
    ["_"] = true, ["sil"] = true, ["pau"] = true, ["SP"] = true, ["cl"] = true, ["br"] = true
}

-- Lazy-loaded reverse map: stable_ipa → romaji
local _ipa_to_romaji = nil
local _output_symbols = nil

local function ensure_map()
    if _ipa_to_romaji ~= nil then return end
    _ipa_to_romaji = {}
    _output_symbols = {}

    local status, content = pcall(vocal.io.read_text, "lua/data/romaji_to_ipa.txt")
    if not status or not content then
        vocal.log("ja_ipa_to_romaji: failed to load romaji_to_ipa.txt: " .. tostring(content))
        return
    end

    for line in content:gmatch("[^\r\n]+") do
        local first = line:sub(1, 1)
        if first ~= "#" and first ~= "" then
            local eq = line:find("=", 1, true)
            if eq then
                local romaji = line:sub(1, eq - 1)
                local ipa = line:sub(eq + 1)
                if romaji and ipa and romaji ~= "" and ipa ~= "" then
                    -- First mapping wins
                    if _ipa_to_romaji[ipa] == nil then
                        _ipa_to_romaji[ipa] = romaji
                        _output_symbols[romaji] = true
                    end
                end
            end
        end
    end

    -- Explicit overrides (as requested)
    _ipa_to_romaji["j"] = "y"
    _ipa_to_romaji["ʔ"] = "'"
    _ipa_to_romaji["l"] = "l"

    -- Add passthrough symbols as valid outputs
    for sym, _ in pairs(PASSTHROUGH) do
        _output_symbols[sym] = true
    end
end

local function convert_symbol(ipa)
    if type(ipa) ~= "string" then return ipa end
    if PASSTHROUGH[ipa] then return ipa end
    return _ipa_to_romaji[ipa] or ipa
end

function profile.transcode_sequence(phonemes)
    ensure_map()
    local result = {}
    for _, p in ipairs(phonemes) do
        table.insert(result, convert_symbol(p))
    end
    return result
end

function profile.g2p_batch(lines)
    ensure_map()
    local results = {}
    for i = 1, #lines do
        local text = lines[i]
        if type(text) == "string" then
            text = text:match("^%s*(.-)%s*$")
            if text == "" then
                results[i] = ""
            else
                local phonemes = {}
                for p in text:gmatch("%S+") do
                    table.insert(phonemes, p)
                end
                local converted = profile.transcode_sequence(phonemes)
                results[i] = table.concat(converted, " ")
            end
        else
            results[i] = ""
        end
    end
    return results
end

function profile.validate_symbol(symbol)
    ensure_map()
    return (_output_symbols[symbol] == true)
end

-- Eager load
ensure_map()

-- Expose output symbols
profile.supported_symbols = {}
for sym, _ in pairs(_output_symbols) do
    table.insert(profile.supported_symbols, sym)
end

return profile
