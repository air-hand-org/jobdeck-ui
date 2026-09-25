all: setup build

include ./mkfiles/*.mk

.PHONY: setup
setup:
	@aqua i && mise i

.PHONY: build
build: cs-build

.PHONY: fmt
fmt: cs-fmt

.PHONY: lint
lint: cs-lint

.PHONY: check-clean
check-clean:
	@git status --short
	@test -z "$$(git status --porcelain)"

.PHONY: test
test: cs-test
