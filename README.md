UdpFile
使用UDP协议传输大文件的设计方案

已实现
内存映射文件方式读写
基本异步发送和接收

TODO
数据包SeqID不用
过滤SeqID相同的包
独立线程计算分块hash值
独立文件记录分块写入/发送情况，以及hash值
减少switch
字节数组缓冲池
独立线程接收回复
连发并等待回复
服务端回复命令
rename命令
neworfail命令
resume命令
重发校验失败的分块
发送错误数据包的IP进行记录，超过一定次数就封禁