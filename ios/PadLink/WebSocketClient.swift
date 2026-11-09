import Foundation
import Combine

/// 负责与 Windows 服务端的 WebSocket 通信，协议为 JSON 行。
final class WebSocketClient: ObservableObject {
    @Published var isConnected = false
    @Published var latencyMS: Int?

    private var session: URLSession = .shared
    private var task: URLSessionWebSocketTask?
    private var pingTimer: Timer?

    // MARK: - 连接管理

    func connect(to host: String) {
        disconnect()
        let trimmed = host.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let url = URL(string: "ws://\(trimmed):8765") else { return }
        let t = session.webSocketTask(with: url)
        task = t
        t.resume()

        // 用第一条 ping 确认连通性，成功才切换到触控板界面
        sendRaw(["t": "ping", "ts": Date().timeIntervalSince1970 * 1000]) { [weak self] error in
            DispatchQueue.main.async {
                guard let self else { return }
                if error == nil {
                    self.isConnected = true
                    self.receive()
                    self.startPing()
                }
            }
        }
    }

    func disconnect() {
        pingTimer?.invalidate()
        pingTimer = nil
        task?.cancel(with: .goingAway, reason: nil)
        task = nil
        isConnected = false
        latencyMS = nil
    }

    private func fail() {
        pingTimer?.invalidate()
        pingTimer = nil
        task = nil
        isConnected = false
    }

    // MARK: - 心跳

    private func startPing() {
        pingTimer?.invalidate()
        let timer = Timer.scheduledTimer(withTimeInterval: 2.0, repeats: true) { [weak self] _ in
            self?.sendRaw(["t": "ping", "ts": Date().timeIntervalSince1970 * 1000], completion: nil)
        }
        RunLoop.main.add(timer, forMode: .common)
        pingTimer = timer
    }

    // MARK: - 收发

    private func receive() {
        task?.receive { [weak self] result in
            guard let self else { return }
            switch result {
            case .success(let message):
                if case .string(let text) = message,
                   let data = text.data(using: .utf8),
                   let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                   obj["t"] as? String == "pong",
                   let ts = obj["ts"] as? Double {
                    let rtt = Int(Date().timeIntervalSince1970 * 1000 - ts)
                    DispatchQueue.main.async { self.latencyMS = max(0, rtt) }
                }
                self.receive()
            case .failure:
                DispatchQueue.main.async { self.fail() }
            }
        }
    }

    private func sendRaw(_ dict: [String: Any], completion: ((Error?) -> Void)?) {
        guard let task,
              let data = try? JSONSerialization.data(withJSONObject: dict),
              let string = String(data: data, encoding: .utf8) else {
            completion?(WebSocketError.badMessage)
            return
        }
        task.send(.string(string)) { error in completion?(error) }
    }

    private func send(_ dict: [String: Any]) {
        guard isConnected else { return }
        sendRaw(dict) { [weak self] error in
            if error != nil {
                DispatchQueue.main.async { self?.fail() }
            }
        }
    }

    // MARK: - 业务消息

    func move(dx: CGFloat, dy: CGFloat) {
        send(["t": "move", "dx": Double(dx), "dy": Double(dy)])
    }

    func click(_ button: String) {
        send(["t": "click", "btn": button])
    }

    func scroll(dx: CGFloat, dy: CGFloat) {
        send(["t": "scroll", "dx": Double(dx), "dy": Double(dy)])
    }

    func text(_ value: String) {
        send(["t": "text", "value": value])
    }

    func key(_ name: String, count: Int = 1) {
        send(["t": "key", "value": name, "count": count])
    }
}

enum WebSocketError: Error {
    case badMessage
}
