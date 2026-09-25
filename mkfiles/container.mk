CONTAINER_MK := $(lastword $(MAKEFILE_LIST))
CONTAINER_MK_DIR := $(dir $(abspath $(CONTAINER_MK)))

.PHONY: container-build
container-build:
	@cd $(CONTAINER_MK_DIR)../ \
	&& docker build \
		--build-arg GITHUB_ACTOR \
		--secret id=github_packages_token,env=GITHUB_PACKAGES_TOKEN \
		. \
	;
