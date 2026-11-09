import SwiftUI

struct ContentView: View {
    @StateObject private var client = WebSocketClient()

    var body: some View {
        Group {
            if client.isConnected {
                TouchpadView(client: client)
            } else {
                ConnectView(client: client)
            }
        }
    }
}

// MARK: - 连接页

struct ConnectView: View {
    @ObservedObject var client: WebSocketClient
    @AppStorage("host") private var host = ""
    @State private var shake = false

    private let haptic = UIImpactFeedbackGenerator(style: .medium)

    var body: some View {
        VStack(spacing: 24) {
            Spacer()

            Image(systemName: "laptop.and.iphone")
                .font(.system(size: 64))
                .foregroundColor(.accentColor)

            Text("PadLink")
                .font(.largeTitle.bold())

            Text("把 iPhone 变成 Windows 的触控板")
                .foregroundColor(.secondary)

            TextField("电脑 IP 地址（服务端窗口里有）", text: $host)
                .textFieldStyle(.roundedBorder)
                .keyboardType(.numbersAndPunctuation)
                .autocorrectionDisabled()
                .textInputAutocapitalization(.never)
                .padding(.horizontal, 40)

            Button {
                if host.trimmingCharacters(in: .whitespaces).isEmpty {
                    shake = true
                    UIImpactFeedbackGenerator(style: .heavy).impactOccurred()
                } else {
                    haptic.impactOccurred()
                    client.connect(to: host)
                }
            } label: {
                Label("连接电脑", systemImage: "bolt.horizontal.fill")
                    .font(.headline)
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 6)
            }
            .buttonStyle(.borderedProminent)
            .padding(.horizontal, 40)

            Spacer()

            Text("提示：先在电脑上启动 PadLink 服务端，\n并保持手机和电脑在同一 WiFi")
                .font(.footnote)
                .foregroundColor(.secondary)
                .multilineTextAlignment(.center)
                .padding(.bottom, 24)
        }
        .offset(x: shake ? -8 : 0)
        .animation(.default, value: shake)
    }
}

// MARK: - 触控板页

struct TouchpadView: View {
    @ObservedObject var client: WebSocketClient

    @State private var lastPoint: CGPoint?
    @State private var touchStart = Date()
    @State private var scrollLastY: CGFloat?
    @State private var scrollAccum: CGFloat = 0
    @State private var keyboardOn = false
    @State private var keyboardText = ""
    @State private var sentText = ""
    @FocusState private var kbFocused: Bool

    /// 手指移动到鼠标像素的放大倍数
    private let sensitivity: CGFloat = 1.6

    private let hapticLight = UIImpactFeedbackGenerator(style: .light)
    private let hapticMedium = UIImpactFeedbackGenerator(style: .medium)
    private let hapticTick = UISelectionFeedbackGenerator()

    var body: some View {
        VStack(spacing: 0) {
            header
            padArea
            scrollStrip
            if keyboardOn { keyboardBar }
        }
        .background(Color.black.ignoresSafeArea())
    }

    // MARK: 顶栏

    private var header: some View {
        HStack(spacing: 16) {
            Button {
                keyboardOn.toggle()
                hapticLight.impactOccurred()
                if keyboardOn { kbFocused = true }
            } label: {
                Image(systemName: keyboardOn ? "keyboard.badge.ellipsis" : "keyboard")
                    .foregroundColor(keyboardOn ? .accentColor : .white)
            }
            .frame(width: 44, height: 44)

            Button {
                client.click("right")
                hapticLight.impactOccurred()
            } label: {
                Image(systemName: "computermouse")
                    .foregroundColor(.white)
            }
            .frame(width: 44, height: 44)

            Spacer()

            if let rtt = client.latencyMS {
                Text("\(rtt) ms")
                    .font(.caption.monospacedDigit())
                    .foregroundColor(.green)
            }

            Button {
                hapticMedium.impactOccurred()
                client.disconnect()
            } label: {
                Image(systemName: "xmark.circle.fill")
                    .foregroundColor(.red)
            }
            .frame(width: 44, height: 44)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 6)
    }

    // MARK: 移动区：拖动=移动指针，轻点=左键

    private var padArea: some View {
        RoundedRectangle(cornerRadius: 16)
            .fill(Color(white: 0.12))
            .overlay(
                RoundedRectangle(cornerRadius: 16)
                    .stroke(Color(white: 0.3), lineWidth: 1)
            )
            .overlay(
                Image(systemName: "hand.point.up.left.fill")
                    .font(.largeTitle)
                    .foregroundColor(Color(white: 0.25))
            )
            .padding(.horizontal, 12)
            .padding(.vertical, 8)
            .frame(maxHeight: .infinity)
            .contentShape(Rectangle())
            .gesture(
                DragGesture(minimumDistance: 0)
                    .onChanged { value in
                        if let last = lastPoint {
                            let dx = (value.location.x - last.x) * sensitivity
                            let dy = (value.location.y - last.y) * sensitivity
                            if abs(dx) > 0.4 || abs(dy) > 0.4 {
                                client.move(dx: dx, dy: dy)
                            }
                        } else {
                            touchStart = Date()
                        }
                        lastPoint = value.location
                    }
                    .onEnded { value in
                        let moved = abs(value.translation.width) < 10
                            && abs(value.translation.height) < 10
                        if moved, Date().timeIntervalSince(touchStart) < 0.35 {
                            client.click("left")
                            hapticLight.impactOccurred()
                        }
                        lastPoint = nil
                    }
            )
    }

    // MARK: 滚动区

    private var scrollStrip: some View {
        RoundedRectangle(cornerRadius: 16)
            .fill(Color(white: 0.08))
            .overlay(
                Image(systemName: "arrow.up.arrow.down")
                    .foregroundColor(Color(white: 0.3))
            )
            .padding(.horizontal, 12)
            .padding(.bottom, 8)
            .frame(height: 96)
            .contentShape(Rectangle())
            .gesture(
                DragGesture(minimumDistance: 0)
                    .onChanged { value in
                        if let last = scrollLastY {
                            let dy = value.location.y - last
                            scrollAccum += abs(dy)
                            client.scroll(dx: 0, dy: dy * 2)
                            if scrollAccum > 24 {
                                hapticTick.selectionChanged()
                                scrollAccum = 0
                            }
                        }
                        scrollLastY = value.location.y
                    }
                    .onEnded { _ in scrollLastY = nil }
            )
    }

    // MARK: 键盘区：增量发送输入内容

    private var keyboardBar: some View {
        VStack(spacing: 8) {
            TextField("在此输入，文字会实时打到电脑上", text: $keyboardText)
                .textFieldStyle(.roundedBorder)
                .focused($kbFocused)
                .autocorrectionDisabled()
                .onChange(of: keyboardText) { newValue in
                    sendKeyboardDelta(newValue)
                }

            HStack(spacing: 20) {
                Button("回车") {
                    client.key("return")
                    hapticLight.impactOccurred()
                }
                Button("退格") {
                    if keyboardText.isEmpty {
                        client.key("backspace")
                        hapticLight.impactOccurred()
                    } else {
                        // 删掉末尾字符，onChange 会自动补发退格
                        keyboardText.removeLast()
                    }
                }
                Spacer()
                Button("收起") {
                    keyboardOn = false
                    kbFocused = false
                }
            }
            .buttonStyle(.borderless)
            .font(.subheadline)
        }
        .padding(.horizontal, 12)
        .padding(.bottom, 6)
        .background(Color(white: 0.12))
    }

    /// 对比上次已发送内容，只把增量发给电脑
    private func sendKeyboardDelta(_ newValue: String) {
        if newValue.hasPrefix(sentText), newValue.count >= sentText.count {
            let added = String(newValue.dropFirst(sentText.count))
            if !added.isEmpty {
                client.text(added)
            }
        } else {
            // 内容被替换/清空（含中文候选词上屏），按退格差值处理
            let removed = sentText.count - newValue.count
            if removed > 0 {
                client.key("backspace", count: removed)
            }
            if !newValue.isEmpty {
                client.text(newValue)
            }
        }
        sentText = newValue
    }
}
