from __future__ import annotations


# The 276-DIP speech bubble leaves about 240 DIPs after horizontal padding.  At
# the supported 15-DIP semibold CJK font, 42 code points stay within the product
# contract of at most three wrapped lines with punctuation/Latin-width margin.
# Inventory size is deliberately not part of this UX-derived boundary.
MAX_SURFACE_TEXT_LENGTH = 42

# These phrases request or strongly invite a response even without a question mark.
IMPLICIT_QUESTION_MARKERS = ("是不是", "好不好")
REPLY_HOOK_MARKERS = (
    "难受就说",
    "拿来跟我显摆",
    "说给我听",
    "讲给我听",
)

# These clauses assert body/environment state unavailable to the runtime.
UNAVAILABLE_STATE_MARKERS = (
    "饭点到了",
    "困得眼睛都睁不开",
    "屏幕亮成这样",
    "连续工作这么久",
    "咖啡喝太晚今晚又要睡不着",
    "手腕酸了",
    "睡前还盯着报错",
    "胃不舒服还空腹扛着",
    "空调吹久了",
)

# Deictic technical nouns claim knowledge of the user's current code, process,
# machine, or environment.  The offline runtime has no evidence for that claim.
TECHNICAL_DEICTIC_OBJECT_MARKERS = (
    "这个接口",
    "这个测试",
    "这个函数",
    "这个方法",
    "这个类",
    "这个模块",
    "这个服务",
    "这个项目",
    "这个仓库",
    "这个分支",
    "这个进程",
    "这个线程",
    "这个查询",
    "这个脚本",
    "这个配置",
    "这个依赖",
    "这个环境",
    "这个端口",
    "这段代码",
    "这条日志",
    "这份日志",
    "这台机器",
    "这次构建",
    "这次部署",
)
TECHNICAL_USER_ENVIRONMENT_MARKERS = (
    "你机器上",
    "你的机器",
    "你电脑上",
    "你的电脑",
    "你本地",
    "你的本地",
    "你环境里",
    "你的环境",
)


__all__ = [
    "IMPLICIT_QUESTION_MARKERS",
    "MAX_SURFACE_TEXT_LENGTH",
    "REPLY_HOOK_MARKERS",
    "TECHNICAL_DEICTIC_OBJECT_MARKERS",
    "TECHNICAL_USER_ENVIRONMENT_MARKERS",
    "UNAVAILABLE_STATE_MARKERS",
]
