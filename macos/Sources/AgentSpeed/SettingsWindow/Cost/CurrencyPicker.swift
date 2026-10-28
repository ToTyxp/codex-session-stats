import AgentSpeedCore
import SwiftUI

/// The display currency (DESIGN 9, Currency): a button showing `code · name` that opens a searchable list. The search
/// field has focus; typing filters by code, localized and English name (start of any word, case-, width- and
/// diacritic-insensitive); ↑ / ↓ move, Return selects, Escape closes.
struct CurrencyPicker: View {
    let model: AppModel

    @State private var presented = false

    var body: some View {
        let cost = model.cost
        let currency = cost.currency
        Button {
            presented.toggle()
        } label: {
            HStack(spacing: 6) {
                Text(currency.code)
                    .fontWeight(.semibold)
                    .monospacedDigit()
                Text("·")
                    .foregroundStyle(.tertiary)
                Text(cost.currencyName)
                    .lineLimit(1)
                Image(systemName: "chevron.up.chevron.down")
                    .font(.system(size: 9, weight: .semibold))
                    .foregroundStyle(.secondary)
                    .padding(.leading, 2)
            }
            .padding(.horizontal, 2)
        }
        .accessibilityLabel("\(model.l10n("settings.currency")), \(currency.code), \(cost.currencyName)")
        .popover(isPresented: $presented, arrowEdge: .bottom) {
            CurrencyList(model: model, select: { code in
                if model.settings.currency != code {
                    withAnimation(.easeOut(duration: 0.2)) { model.settings.currency = code }
                }
                presented = false
            })
        }
    }
}

/// The popover: a search field above the grouped list (≈ 320 × 400 pt).
struct CurrencyList: View {
    let model: AppModel
    let select: (String) -> Void

    @State private var query: String

    init(model: AppModel, query: String = "", select: @escaping (String) -> Void) {
        self.model = model
        self.select = select
        _query = State(initialValue: query)
    }
    @State private var highlighted: String?
    @FocusState private var searchFocused: Bool

    private struct Group: Identifiable {
        var id: String { title }
        var title: String
        var currencies: [Currency]
    }

    var body: some View {
        let l10n = model.l10n
        let groups = groups
        let flat = groups.flatMap(\.currencies).map(\.code)
        VStack(spacing: 0) {
            HStack(spacing: 6) {
                Image(systemName: "magnifyingglass")
                    .foregroundStyle(.secondary)
                    .accessibilityHidden(true)
                TextField(l10n("settings.currency.search"), text: $query)
                    .textFieldStyle(.plain)
                    .focused($searchFocused)
                    .onSubmit { if let code = highlighted ?? flat.first { select(code) } }
                    .onKeyPress(.downArrow) {
                        move(1, in: flat)
                        return .handled
                    }
                    .onKeyPress(.upArrow) {
                        move(-1, in: flat)
                        return .handled
                    }
                if !query.isEmpty {
                    Button {
                        query = ""
                    } label: {
                        Image(systemName: "xmark.circle.fill")
                            .foregroundStyle(.tertiary)
                    }
                    .buttonStyle(.plain)
                    .accessibilityLabel(l10n("history.cancel"))
                }
            }
            .padding(.horizontal, Spacing.s)
            .padding(.vertical, 7)
            .background(Color.primary.opacity(0.05), in: RoundedRectangle(cornerRadius: 7, style: .continuous))
            .padding(Spacing.s)

            Divider()

            if flat.isEmpty {
                Text(l10n("settings.currency.noResults"))
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                ScrollViewReader { proxy in
                    List {
                        ForEach(groups) { group in
                            Section {
                                ForEach(group.currencies) { currency in
                                    row(currency)
                                        .id(currency.code)
                                }
                            } header: {
                                if !group.title.isEmpty {
                                    Text(group.title)
                                        .font(.system(size: 11, weight: .semibold))
                                        .foregroundStyle(.secondary)
                                }
                            }
                        }
                    }
                    .listStyle(.plain)
                    .scrollContentBackground(.hidden)
                    .onChange(of: highlighted) { _, code in
                        guard let code else { return }
                        withAnimation(.easeOut(duration: 0.12)) { proxy.scrollTo(code) }
                    }
                    .onAppear { proxy.scrollTo(model.settings.currency, anchor: .center) }
                }
            }
        }
        .frame(width: 320, height: 400)
        .onAppear {
            searchFocused = true
            highlighted = flat.contains(model.settings.currency) ? model.settings.currency : flat.first
        }
        .onChange(of: query) { highlighted = groups.first?.currencies.first?.code }
        .environment(\.locale, model.l10n.locale)
    }

    private var groups: [Group] {
        let l10n = model.l10n
        let cost = model.cost
        let table = cost.currencies
        let language = cost.language
        let name: (Currency) -> String = { cost.name(of: $0) }
        if !query.trimmingCharacters(in: .whitespaces).isEmpty {
            let found = table.search(query, language: language, localizedName: name, englishName: { cost.englishName(of: $0) })
            return found.isEmpty ? [] : [Group(title: "", currencies: found)]
        }
        return [
            Group(title: l10n("settings.currency.common"), currencies: table.commonGroup(language: language)),
            Group(title: l10n("settings.currency.all"), currencies: table.otherGroup(language: language, localizedName: name)),
        ]
    }

    private func move(_ step: Int, in codes: [String]) {
        guard !codes.isEmpty else { return }
        let index = highlighted.flatMap { codes.firstIndex(of: $0) } ?? (step > 0 ? -1 : codes.count)
        highlighted = codes[(index + step).clamped(to: 0...(codes.count - 1))]
    }

    private func row(_ currency: Currency) -> some View {
        let cost = model.cost
        let current = currency.code == model.settings.currency
        let isHighlighted = currency.code == highlighted
        return Button {
            select(currency.code)
        } label: {
            HStack(spacing: Spacing.s) {
                Image(systemName: "checkmark")
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundStyle(Color.accentColor)
                    .opacity(current ? 1 : 0)
                    .frame(width: 12)
                    .accessibilityHidden(true)
                Text(currency.code)
                    .font(.system(size: 13, weight: .semibold).monospacedDigit())
                    .frame(width: 38, alignment: .leading)
                Text(cost.name(of: currency))
                    .font(.system(size: 13))
                    .lineLimit(1)
                Spacer(minLength: Spacing.xs)
                Text(currency.symbol(language: cost.language))
                    .font(.system(size: 12))
                    .foregroundStyle(.secondary)
            }
            .padding(.vertical, 3)
            .padding(.horizontal, 6)
            .background(
                RoundedRectangle(cornerRadius: 5, style: .continuous)
                    .fill(isHighlighted ? Color.accentColor.opacity(0.16) : .clear)
            )
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { if $0 { highlighted = currency.code } }
        .listRowInsets(EdgeInsets(top: 0, leading: 4, bottom: 0, trailing: 4))
        .listRowSeparator(.hidden)
        .accessibilityLabel("\(currency.code), \(cost.name(of: currency))")
        .accessibilityAddTraits(current ? .isSelected : [])
    }
}
