# ── Build stage ────────────────────────────────────────────────────────
FROM rust:latest AS builder
WORKDIR /app

# Cache dependency compilation in a separate layer
COPY Cargo.toml ./
RUN mkdir src && echo "fn main() {}" > src/main.rs
RUN cargo build --release
RUN rm -rf src

# Build the real binary
COPY src/ src/
RUN touch src/main.rs && cargo build --release

# ── Runtime stage ─────────────────────────────────────────────────────
FROM debian:bookworm-slim
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates \
    && rm -rf /var/lib/apt/lists/*

COPY --from=builder /app/target/release/sub-pub-rust /usr/local/bin/sub-pub-rust

ENTRYPOINT ["sub-pub-rust"]

# docker build -t registry.gitlab.com/gcoder.ninhph/sub-pub-c:rust-version .
# docker push registry.gitlab.com/gcoder.ninhph/sub-pub-c:rust-version
