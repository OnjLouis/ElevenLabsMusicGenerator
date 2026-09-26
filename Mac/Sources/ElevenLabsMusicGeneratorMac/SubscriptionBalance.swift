import Foundation

struct SubscriptionBalance: Decodable {
    let characterCount: Int64
    let characterLimit: Int64
    let nextResetUnix: Int64?

    enum CodingKeys: String, CodingKey {
        case characterCount = "character_count"
        case characterLimit = "character_limit"
        case nextResetUnix = "next_character_count_reset_unix"
    }

    var remaining: Int64 { max(0, characterLimit - characterCount) }

    static func decode(_ data: Data) throws -> Self {
        let value = try JSONDecoder().decode(Self.self, from: data)
        guard value.characterCount >= 0, value.characterLimit >= 0,
              value.nextResetUnix.map({ $0 > 0 && $0 <= 253_402_300_799 }) ?? true else {
            throw MusicError.response("ElevenLabs returned an invalid subscription count.")
        }
        return value
    }

    func display(now: Date = Date()) -> String {
        let left = NumberFormatter.localizedString(from: NSNumber(value: remaining), number: .decimal)
        let limit = NumberFormatter.localizedString(from: NSNumber(value: characterLimit), number: .decimal)
        let used = NumberFormatter.localizedString(from: NSNumber(value: characterCount), number: .decimal)
        var result = "Included credits remaining: \(left) of \(limit).\nUsed this period: \(used)."
        guard let nextResetUnix, nextResetUnix > 0 else { return result + "\nNext reset: unavailable." }
        let reset = Date(timeIntervalSince1970: TimeInterval(nextResetUnix))
        let date = DateFormatter()
        date.dateStyle = .medium
        date.timeStyle = .short
        date.timeZone = .current
        result += "\nNext reset: \(date.string(from: reset)) \(TimeZone.current.abbreviation() ?? "local time")."
        let seconds = reset.timeIntervalSince(now)
        if seconds > 0 {
            let minutes = Int(ceil(seconds / 60))
            let days = minutes / 1440
            let hours = minutes % 1440 / 60
            var parts: [String] = []
            if days > 0 { parts.append("\(days) \(days == 1 ? "day" : "days")") }
            if hours > 0 { parts.append("\(hours) \(hours == 1 ? "hour" : "hours")") }
            if minutes % 60 > 0 { parts.append("\(minutes % 60) \(minutes % 60 == 1 ? "minute" : "minutes")") }
            result += " Approximately \(parts.joined(separator: ", ")) remaining."
        }
        return result
    }
}
