-- Converts ARPABET (CMU style) → IPA (e.g., AX → ə, AA → ɑ)
-- Bidirectional with en_ipa_to_arpa.lua using same ipa_to_arpa.txt
-- Uses "_" as universal silence (no distinction between sil/sp/pau).

local profile = {}

profile.id           = "en_arpa_to_ipa"
profile.display_name = "English (ARPABET → IPA)"
profile.encoding     = "IPA"

-- Passthrough symbols (unchanged in output)
local PASSTHROUGH = {
    ["_"] = true, ["sil"] = true, ["sp"] = true, ["pau"] = true
}

-- Lazy-loaded map: arpabet → IPA (reverse of ipa_to_arpa.txt)
local _arpa_to_ipa = nil
local _output_symbols = nil

local function ensure_map()
    if _arpa_to_ipa ~= nil then return end
    _arpa_to_ipa = {}
    _output_symbols = {}

    local map_path = "lua/data/ipa_to_arpa.txt"

    if not vocal.io.file_exists(map_path) then
        vocal.log("en_arpa_to_ipa: map file not found at " .. map_path)
        return
    end

    local content = vocal.io.read_text(map_path)

    -- First, parse IPA→ARPABET mappings (first wins)
    local ipa_to_arpa_temp = {}

    for line in content:gmatch("[^\r\n]+") do
        local first = line:sub(1, 1)
        if first ~= "#" and first ~= "" then
            local eq = line:find("=", 1, true)
            if eq then
                local ipa = line:sub(1, eq - 1)
                local arpa = line:sub(eq + 1)
                if ipa and arpa and ipa ~= "" and arpa ~= "" then
                    if ipa_to_arpa_temp[ipa] == nil then
                        ipa_to_arpa_temp[ipa] = arpa
                    end
                end
            end
        end
    end

    -- Now build reverse map (arpa → IPA), using first occurrence only
    for ipa, arpa in pairs(ipa_to_arpa_temp) do
        if _arpa_to_ipa[arpa] == nil then
            _arpa_to_ipa[arpa] = ipa
            _output_symbols[ipa] = true
        end
    end

    -- Explicit overrides for silence normalization (ensure symmetry)
    _arpa_to_ipa["AX"] = "ə"
    for _, sym in ipairs({ "sil", "sp", "pau", "_" }) do
        _arpa_to_ipa[sym] = "_"
        _output_symbols["_"] = true
    end

    -- Passthrough ARPABET symbols as valid outputs
    for sym, _ in pairs(PASSTHROUGH) do
        _output_symbols[sym] = true
    end
end

local function convert_symbol(arpa)
    if type(arpa) ~= "string" then return arpa end
    if PASSTHROUGH[arpa] then return "_" end
    return _arpa_to_ipa[arpa] or arpa
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

-- Eager load (for UI and batch processing)
ensure_map()

-- Expose output symbols
profile.supported_symbols = {}
for sym, _ in pairs(_output_symbols) do
    table.insert(profile.supported_symbols, sym)
end

return profile
