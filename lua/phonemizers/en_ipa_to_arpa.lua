-- Converts stable IPA → ARPABET (CMU-style) for English.
-- Based on: CMU Pronouncing Dictionary + AX extension.
-- Unified silence symbol: "_" (replaces both "sil" and "sp").

local profile = {}

profile.id           = "en_ipa_to_arpa"
profile.display_name = "English (IPA → ARPABET/CMU)"
profile.encoding     = "ARPABET"

-- Passthrough symbols (unchanged in output)
local PASSTHROUGH = {
    ["_"] = true, ["sil"] = true, ["sp"] = true, ["pau"] = true
}

-- Lazy-loaded reverse map: IPA → ARPABET
local _ipa_to_arpa = nil
local _output_symbols = nil

local function ensure_map()
    if _ipa_to_arpa ~= nil then return end
    _ipa_to_arpa = {}
    _output_symbols = {}

    -- Read the IPA→ARPABET mapping file (you'll need to create this file)
    -- Example content: "ɑ=AA; æ=AE; ʌ=AX; ..."
    local map_path = "lua/data/ipa_to_arpa.txt"

    if not vocal.io.file_exists(map_path) then
        vocal.log("en_ipa_to_arpa: map file not found at " .. map_path)
        return
    end

    local content = vocal.io.read_text(map_path)
    for line in content:gmatch("[^\r\n]+") do
        local first = line:sub(1, 1)
        if first ~= "#" and first ~= "" then
            local eq = line:find("=", 1, true)
            if eq then
                local ipa = line:sub(1, eq - 1)
                local arpa = line:sub(eq + 1)
                if ipa and arpa and ipa ~= "" and arpa ~= "" then
                    -- First mapping wins
                    if _ipa_to_arpa[ipa] == nil then
                        _ipa_to_arpa[ipa] = arpa
                        _output_symbols[arpa] = true
                    end
                end
            end
        end
    end

    -- Explicit fallbacks (e.g., stress numbers, diacritics)
    _ipa_to_arpa["ə"] = "AX"  -- schwa → AX (CMU standard)

    -- Silence normalization
    for _, sym in ipairs({ "sil", "sp", "pau" }) do
        _ipa_to_arpa[sym] = "_"
        _output_symbols["_"] = true
    end

    -- Add passthrough symbols as valid outputs
    for sym, _ in pairs(PASSTHROUGH) do
        _output_symbols[sym] = true
    end
end

local function convert_symbol(ipa)
    if type(ipa) ~= "string" then return ipa end
    if PASSTHROUGH[ipa] then return "_" end
    return _ipa_to_arpa[ipa] or ipa
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
                results[i] = "_"
            else
                local phonemes = {}
                for p in text:gmatch("%S+") do
                    table.insert(phonemes, p)
                end
                local converted = profile.transcode_sequence(phonemes)
                results[i] = table.concat(converted, " ")
            end
        else
            results[i] = "_"
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
