import Foundation

struct PublishedMacRelease {
    let version: String
    let page: URL
}

enum MacPackageArchitecture {
    case appleSilicon
    case intel

    static var current: Self {
        #if arch(x86_64)
        return .intel
        #else
        return .appleSilicon
        #endif
    }

    func assetName(version: String) -> String {
        switch self {
        case .appleSilicon: return "ElevenLabs-Music-Generator-Mac-\(version).zip"
        case .intel: return "ElevenLabs-Music-Generator-Mac-Intel-\(version).zip"
        }
    }
}

enum ReleaseCatalog {
    static func newerMacRelease(in data: Data, currentVersion: String,
                                architecture: MacPackageArchitecture = .current) throws -> PublishedMacRelease? {
        guard let releases = try JSONSerialization.jsonObject(with: data) as? [[String: Any]],
              let current = versionParts(currentVersion) else {
            throw MusicError.response("The release list could not be read.")
        }
        return releases.compactMap { item -> (release: PublishedMacRelease, parts: [Int])? in
            guard item["draft"] as? Bool == false,
                  item["prerelease"] as? Bool == false,
                  let tag = item["tag_name"] as? String,
                  let parts = versionParts(tag), current.lexicographicallyPrecedes(parts),
                  let pageText = item["html_url"] as? String,
                  let page = URL(string: pageText), page.scheme == "https", page.host == "github.com",
                  let assets = item["assets"] as? [[String: Any]],
                  assets.contains(where: { asset in
                      guard let name = asset["name"] as? String else { return false }
                      return name == architecture.assetName(version: parts.map(String.init).joined(separator: "."))
                  }) else { return nil }
            return (PublishedMacRelease(version: parts.map(String.init).joined(separator: "."), page: page), parts)
        }.max(by: { $0.parts.lexicographicallyPrecedes($1.parts) })?.release
    }

    private static func versionParts(_ text: String) -> [Int]? {
        let clean = text.trimmingCharacters(in: CharacterSet(charactersIn: "vV"))
        let parts = clean.split(separator: ".").compactMap { Int($0) }
        return parts.count == 3 ? parts : nil
    }
}
